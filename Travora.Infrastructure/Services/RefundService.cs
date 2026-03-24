using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travora.Application.DTOs.Refunds;
using Travora.Application.Interfaces.Services;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Domain.Constants;
using Travora.Infrastructure.Data;
using Travora.Shared.Settings;

namespace Travora.Infrastructure.Services;

public class RefundService : IRefundService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PaymobSettings _settings;
    private readonly INotificationPusher _pusher;

    public RefundService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<PaymobSettings> settings,
        INotificationPusher pusher)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _pusher = pusher;
    }

    public async Task<RefundResponse> RequestRefundAsync(int customerId, int orderId, RefundRequest request)
    {
        var order = await _db.Orders
            .Include(o => o.Invoices)
            .Include(o => o.Package)
            .Include(o => o.OrderServices)
                .ThenInclude(os => os.PackageService)
                    .ThenInclude(ps => ps.Service)
            .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

        if (order == null)
            return new RefundResponse { Success = false, Message = "الأوردر مش موجود" };

        if (order.OrderStatus == OrderStatus.Completed)
            return new RefundResponse { Success = false, Message = "لا يمكن استرداد أوردر تم تنفيذه بالكامل" };

        if (order.OrderStatus == OrderStatus.Cancelled)
            return new RefundResponse { Success = false, Message = "الأوردر ملغي بالفعل" };

        if (order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.InProgress or OrderStatus.rescheduled))
            return new RefundResponse { Success = false, Message = "لا يمكن طلب استرداد لهذا الأوردر في حالته الحالية" };

        var invoice = order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Paid);
        if (invoice == null)
            return new RefundResponse { Success = false, Message = "لا يوجد فاتورة مدفوعة لهذا الأوردر" };

        var existingRefund = await _db.Refunds
            .AnyAsync(r => r.OrderId == orderId && r.RefundStatus == RefundStatus.Requested);
        if (existingRefund)
            return new RefundResponse { Success = false, Message = "يوجد طلب استرداد معلق بالفعل" };

        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.InvoiceId == invoice.InvoiceId && p.PaymentStatus == PaymentStatus.Completed);
        if (payment == null)
            return new RefundResponse { Success = false, Message = "لا يوجد عملية دفع مكتملة" };

        var refund = new Refund
        {
            RefundAmount = invoice.TotalAmount,
            RefundStatus = RefundStatus.Requested,
            Reason = request.Reason,
            OrderId = orderId,
            PaymentId = payment.PaymentId,
            Order = order,
            Payment = payment
        };
        _db.Refunds.Add(refund);

        var pkgName = order.Package?.PackageName ?? string.Empty;

        if (pkgName == PackageNames.DoorToDoor || 
            pkgName == PackageNames.CarServiceToAirport || 
            pkgName == PackageNames.CarServiceFromAirport)
        {
            var services = order.OrderServices.ToList();
            bool hasStarted = services.Any(s => s.ServiceStatus is ServiceStatus.InProgress or ServiceStatus.Completed);
            bool hasPending = services.Any(s => s.ServiceStatus is ServiceStatus.Pending or ServiceStatus.Assigned or ServiceStatus.Cancelled);

            if (!hasStarted && hasPending)
            {
                return await ExecutePaymobRefundAsync(
                    refund,
                    invoice.TotalAmount,
                    adminId: null,
                    title: "تم استرداد المبلغ بالكامل",
                    message: $"تم استرداد مبلغ {invoice.TotalAmount} جنيه بنجاح لإلغاء الطلب",
                    isPartial: false);
            }
            else if (hasStarted && !hasPending)
            {
                var now = DateTime.UtcNow;
                refund.RefundStatus = RefundStatus.Rejected;
                refund.ProcessedAt = now;
                refund.AdminNotes = "مرفوض تلقائياً: لا يمكن استرداد المبلغ بعد بدء تنفيذ جميع الخدمات";
                await _db.SaveChangesAsync();

                _db.Notifications.Add(new Notification
                {
                    UserId = order.CustomerId,
                    UserType = UserType.Customer,
                    NotificationType = NotificationType.OrderUpdated,
                    Title = "تم رفض طلب الاسترداد",
                    Message = refund.AdminNotes,
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = order.OrderId
                });
                await _db.SaveChangesAsync();

                await _pusher.PushToCustomerAsync(
                    order.CustomerId,
                    "تم رفض طلب الاسترداد",
                    refund.AdminNotes,
                    "RefundRejected",
                    order.OrderId);

                return new RefundResponse { Success = false, Message = "لا يمكن استرداد المبلغ بعد بدء تنفيذ جميع الخدمات" };
            }
            else if (hasStarted && hasPending)
            {
                decimal partialAmount = services
                    .Where(s => s.ServiceStatus is ServiceStatus.Pending or ServiceStatus.Assigned or ServiceStatus.Cancelled)
                    .Sum(s => s.PackageService.Service.BasePrice);

                return await ExecutePaymobRefundAsync(
                    refund,
                    partialAmount,
                    adminId: null,
                    title: "تم استرداد جزئي للمبلغ",
                    message: $"تم استرداد مبلغ {partialAmount} جنيه بنجاح للخدمات غير المنفذة",
                    isPartial: true);
            }
        }
        else if (pkgName == PackageNames.TrackingBaggage)
        {
            // Bag Tracking مفيش موظفين → full refund أوتوماتيك دايماً
            return await ExecutePaymobRefundAsync(
                refund,
                invoice.TotalAmount,
                adminId: null,
                title: "تم استرداد المبلغ بالكامل",
                message: $"تم استرداد مبلغ {invoice.TotalAmount} جنيه بنجاح",
                isPartial: false);
        }

        await _db.SaveChangesAsync();

        return new RefundResponse
        {
            Success = true,
            RefundId = refund.RefundId,
            Status = refund.RefundStatus.ToString(),
            Message = "تم تقديم طلب الاسترداد بنجاح، سيتم مراجعته من الإدارة"
        };
    }

    public async Task<RefundStatusResponse?> GetRefundStatusAsync(int customerId, int orderId)
    {
        var refund = await _db.Refunds
            .Where(r => r.OrderId == orderId && r.Order.CustomerId == customerId)
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync();

        if (refund == null)
            return null;

        return new RefundStatusResponse
        {
            RefundId = refund.RefundId,
            Status = refund.RefundStatus.ToString(),
            Amount = refund.RefundAmount,
            RequestedAt = refund.RequestedAt,
            ProcessedAt = refund.ProcessedAt,
            Reason = refund.Reason,
            AdminNotes = refund.AdminNotes
        };
    }

    public async Task<List<AdminRefundListItem>> GetAllRefundsAsync()
    {
        return await _db.Refunds
            .Include(r => r.Order)
                .ThenInclude(o => o.Customer)
            .OrderByDescending(r => r.RequestedAt)
            .Select(r => new AdminRefundListItem
            {
                RefundId = r.RefundId,
                CustomerName = r.Order.Customer.Firstname + " " + r.Order.Customer.Lastname,
                OrderId = r.OrderId,
                Amount = r.RefundAmount,
                Status = r.RefundStatus.ToString(),
                RequestedAt = r.RequestedAt
            })
            .ToListAsync();
    }

    public async Task<AdminRefundDetail> GetRefundDetailAsync(int refundId)
    {
        var refund = await _db.Refunds
            .Include(r => r.Order)
                .ThenInclude(o => o.Customer)
            .Include(r => r.ProcessedByAdmin)
            .FirstOrDefaultAsync(r => r.RefundId == refundId)
            ?? throw new KeyNotFoundException("طلب الاسترداد مش موجود");

        return new AdminRefundDetail
        {
            RefundId = refund.RefundId,
            OrderId = refund.OrderId,
            CustomerName = refund.Order.Customer.Firstname + " " + refund.Order.Customer.Lastname,
            CustomerEmail = refund.Order.Customer.Email,
            CustomerPhone = refund.Order.Customer.PhoneNumber,
            OrderAmount = refund.Order.TotalAmount,
            RefundAmount = refund.RefundAmount,
            Status = refund.RefundStatus.ToString(),
            Reason = refund.Reason,
            AdminNotes = refund.AdminNotes,
            RequestedAt = refund.RequestedAt,
            ProcessedAt = refund.ProcessedAt,
            ProcessedByAdmin = refund.ProcessedByAdmin?.FullName
        };
    }

    public async Task<RefundResponse> ApproveRefundAsync(int adminId, int refundId)
    {
        var refund = await _db.Refunds
            .Include(r => r.Payment)
            .Include(r => r.Order)
                .ThenInclude(o => o.Invoices)
            .FirstOrDefaultAsync(r => r.RefundId == refundId)
            ?? throw new KeyNotFoundException("طلب الاسترداد مش موجود");

        if (refund.RefundStatus != RefundStatus.Requested)
            return new RefundResponse { Success = false, Message = "طلب الاسترداد مش في حالة انتظار" };

        return await ExecutePaymobRefundAsync(
            refund,
            refund.RefundAmount,
            adminId,
            "تم الموافقة على طلب الاسترداد",
            $"تم استرداد مبلغ {refund.RefundAmount} جنيه بنجاح",
            isPartial: false);
    }

    private async Task<RefundResponse> ExecutePaymobRefundAsync(Refund refund, decimal amount, int? adminId, string title, string message, bool isPartial)
    {
        var now = DateTime.UtcNow;

        try
        {
            // Step 1: Get Paymob auth token
            var client = _httpClientFactory.CreateClient("Paymob");
            var authPayload = new { api_key = _settings.ApiKey };
            var authResponse = await client.PostAsJsonAsync($"{_settings.BaseUrl}/api/auth/tokens", authPayload);
            authResponse.EnsureSuccessStatusCode();
            var authResult = await authResponse.Content.ReadFromJsonAsync<JsonElement>();
            var authToken = authResult.GetProperty("token").GetString()!;

            // Step 2: Call Paymob refund API
            var amountCents = (int)(amount * 100);
            var refundPayload = new
            {
                auth_token = authToken,
                transaction_id = refund.Payment.TransactionId,
                amount_cents = amountCents
            };
            var refundResponse = await client.PostAsJsonAsync(
                $"{_settings.BaseUrl}/api/acceptance/void_refund/refund", refundPayload);
            refundResponse.EnsureSuccessStatusCode();

            var refundResult = await refundResponse.Content.ReadFromJsonAsync<JsonElement>();
            var paymobRefundId = refundResult.GetProperty("id").GetInt64().ToString();

            // Success → update statuses
            refund.RefundStatus = RefundStatus.Processed;
            refund.ProcessedAt = now;
            refund.ProcessedByAdminId = adminId;
            refund.RefundTransactionId = paymobRefundId;
            if (isPartial)
            {
                refund.RefundAmount = amount;
            }
            refund.UpdatedAt = now;

            var invoice = refund.Order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Paid);
            if (invoice != null)
            {
                invoice.InvoiceStatus = InvoiceStatus.Refunded;
                invoice.UpdatedAt = now;
            }

            refund.Order.OrderStatus = OrderStatus.Cancelled;
            refund.Order.CancellationReason = "تم إلغاء الأوردر بسبب استرداد المبلغ";
            refund.Order.UpdatedAt = now;

            refund.Payment.PaymentStatus = PaymentStatus.Refunded;
            refund.Payment.UpdatedAt = now;

            // DB notification
            _db.Notifications.Add(new Notification
            {
                UserId = refund.Order.CustomerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.OrderUpdated,
                Title = title,
                Message = message,
                NotificationChannel = NotificationChannel.InApp,
                OrderId = refund.OrderId
            });

            await _db.SaveChangesAsync();

            // Real-time push
            await _pusher.PushToCustomerAsync(
                refund.Order.CustomerId,
                title,
                message,
                "RefundApproved",
                refund.OrderId);

            return new RefundResponse
            {
                Success = true,
                RefundId = refund.RefundId,
                Status = RefundStatus.Processed.ToString(),
                Message = "تم الاسترداد بنجاح"
            };
        }
        catch
        {
            // Paymob failed → mark refund as failed, don't change Order
            refund.RefundStatus = RefundStatus.Failed;
            refund.ProcessedAt = now;
            refund.ProcessedByAdminId = adminId;
            refund.UpdatedAt = now;
            await _db.SaveChangesAsync();

            return new RefundResponse
            {
                Success = false,
                RefundId = refund.RefundId,
                Status = RefundStatus.Failed.ToString(),
                Message = "فشل الاسترداد من بوابة الدفع، يرجى المحاولة لاحقاً"
            };
        }
    }

    public async Task<RefundResponse> RejectRefundAsync(int adminId, int refundId, AdminProcessRefundRequest request)
    {
        var refund = await _db.Refunds
            .Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.RefundId == refundId)
            ?? throw new KeyNotFoundException("طلب الاسترداد مش موجود");

        if (refund.RefundStatus != RefundStatus.Requested)
            return new RefundResponse { Success = false, Message = "طلب الاسترداد مش في حالة انتظار" };

        var now = DateTime.UtcNow;
        refund.RefundStatus = RefundStatus.Rejected;
        refund.ProcessedAt = now;
        refund.ProcessedByAdminId = adminId;
        refund.AdminNotes = request.Notes;
        refund.UpdatedAt = now;

        // DB notification
        _db.Notifications.Add(new Notification
        {
            UserId = refund.Order.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.OrderUpdated,
            Title = "تم رفض طلب الاسترداد",
            Message = request.Notes ?? "تم رفض طلب الاسترداد",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = refund.OrderId
        });

        await _db.SaveChangesAsync();

        // Real-time push
        await _pusher.PushToCustomerAsync(
            refund.Order.CustomerId,
            "تم رفض طلب الاسترداد",
            request.Notes ?? "تم رفض طلب الاسترداد من الإدارة",
            "RefundRejected",
            refund.OrderId);

        return new RefundResponse
        {
            Success = true,
            RefundId = refund.RefundId,
            Status = RefundStatus.Rejected.ToString(),
            Message = "تم رفض طلب الاسترداد"
        };
    }
}
