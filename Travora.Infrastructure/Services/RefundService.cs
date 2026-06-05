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
            return new RefundResponse { Success = false, Message = "Order not found" };

        if (order.OrderStatus == OrderStatus.Completed)
            return new RefundResponse { Success = false, Message = "Cannot refund a fully completed order" };

        if (order.OrderStatus == OrderStatus.Cancelled)
            return new RefundResponse { Success = false, Message = "Order is already cancelled" };

        if (order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.InProgress or OrderStatus.rescheduled))
            return new RefundResponse { Success = false, Message = "Refund cannot be requested for this order in its current status" };

        var invoice = order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Paid);
        if (invoice == null)
            return new RefundResponse { Success = false, Message = "No paid invoice found for this order" };

        var existingRefund = await _db.Refunds
            .AnyAsync(r => r.OrderId == orderId && r.RefundStatus == RefundStatus.Requested);
        if (existingRefund)
            return new RefundResponse { Success = false, Message = "A pending refund request already exists" };

        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.InvoiceId == invoice.InvoiceId && p.PaymentStatus == PaymentStatus.Completed);
        if (payment == null)
            return new RefundResponse { Success = false, Message = "No completed payment found" };

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
                    title: "Full amount refunded",
                    message: $"Amount of {invoice.TotalAmount} EGP has been successfully refunded for order cancellation",
                    isPartial: false);
            }
            else if (hasStarted && !hasPending)
            {
                var now = DateTime.UtcNow;
                refund.RefundStatus = RefundStatus.Rejected;
                refund.ProcessedAt = now;
                refund.AdminNotes = "Automatically rejected: Refund is not possible after all services have started";
                await _db.SaveChangesAsync();

                _db.Notifications.Add(new Notification
                {
                    UserId = order.CustomerId,
                    UserType = UserType.Customer,
                    NotificationType = NotificationType.OrderUpdated,
                    Title = "Refund request rejected",
                    Message = refund.AdminNotes,
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = order.OrderId
                });
                await _db.SaveChangesAsync();

                await _pusher.PushToCustomerAsync(
                    order.CustomerId,
                    "Refund request rejected",
                    refund.AdminNotes,
                    "RefundRejected",
                    order.OrderId);

                return new RefundResponse { Success = false, Message = "Refund is not possible after all services have started" };
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
                    title: "Partial amount refunded",
                    message: $"Amount of {partialAmount} EGP has been successfully refunded for unexecuted services",
                    isPartial: true);
            }
        }
        else if (pkgName == PackageNames.TrackingBaggage)
        {
            // Bag Tracking no employees -> automatic full refund always
            return await ExecutePaymobRefundAsync(
                refund,
                invoice.TotalAmount,
                adminId: null,
                title: "Full amount refunded",
                message: $"Amount of {invoice.TotalAmount} EGP has been successfully refunded",
                isPartial: false);
        }

        await _db.SaveChangesAsync();

        return new RefundResponse
        {
            Success = true,
            RefundId = refund.RefundId,
            Status = refund.RefundStatus.ToString(),
            Message = "Refund request submitted successfully, it will be reviewed by the administration"
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
                OrderId = r.OrderId,
                CustomerName = r.Order.Customer.Firstname + " " + r.Order.Customer.Lastname,
                CustomerEmail = r.Order.Customer.Email,
                CustomerPhone = r.Order.Customer.PhoneNumber,
                OrderAmount = r.Order.TotalAmount,
                RefundAmount = r.RefundAmount,
                Status = r.RefundStatus.ToString(),
                Reason = r.Reason,
                RequestedAt = r.RequestedAt
            })
            .ToListAsync();
    }
    public async Task<RefundResponse> ApproveRefundAsync(int adminId, int refundId)
    {
        var refund = await _db.Refunds
            .Include(r => r.Payment)
            .Include(r => r.Order)
                .ThenInclude(o => o.Invoices)
            .FirstOrDefaultAsync(r => r.RefundId == refundId)
            ?? throw new KeyNotFoundException("Refund request not found");

        if (refund.RefundStatus != RefundStatus.Requested)
            return new RefundResponse { Success = false, Message = "Refund request is not in pending status" };

        return await ExecutePaymobRefundAsync(
            refund,
            refund.RefundAmount,
            adminId,
            "Refund request approved",
            $"Amount of {refund.RefundAmount} EGP has been successfully refunded",
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
            refund.Order.CancellationReason = "Order cancelled due to refund";
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
                Message = "Refund successful"
            };
        }
        catch
        {
            // Paymob failed → mark refund as failed
            refund.RefundStatus = RefundStatus.Failed;
            refund.ProcessedAt = now;
            refund.ProcessedByAdminId = adminId;
            refund.UpdatedAt = now;

            // Notify ALL active admins about the failed refund
            var adminIds = await _db.Admins
                .Where(a => a.IsActive)
                .Select(a => a.AdminId)
                .ToListAsync();

            foreach (var aid in adminIds)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = aid,
                    UserType = UserType.Admin,
                    NotificationType = NotificationType.OrderUpdated,
                    Title = "Refund Failed — Action Required",
                    Message = $"Refund of {amount:F2} EGP for order #{refund.OrderId} failed. Reason: {refund.Reason}. Please review and retry.",
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = refund.OrderId
                });
            }

            await _db.SaveChangesAsync();

            return new RefundResponse
            {
                Success = false,
                RefundId = refund.RefundId,
                Status = RefundStatus.Failed.ToString(),
                Message = "Refund failed from payment gateway, please try again later"
            };
        }
    }

    public async Task<RefundResponse> RejectRefundAsync(int adminId, int refundId, AdminProcessRefundRequest request)
    {
        var refund = await _db.Refunds
            .Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.RefundId == refundId)
            ?? throw new KeyNotFoundException("Refund request not found");

        if (refund.RefundStatus != RefundStatus.Requested)
            return new RefundResponse { Success = false, Message = "Refund request is not in pending status" };

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
            Title = "Refund request rejected",
            Message = request.Notes ?? "Refund request rejected",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = refund.OrderId
        });

        await _db.SaveChangesAsync();

        // Real-time push
        await _pusher.PushToCustomerAsync(
            refund.Order.CustomerId,
            "Refund request rejected",
            request.Notes ?? "Refund request rejected by administration",
            "RefundRejected",
            refund.OrderId);

        return new RefundResponse
        {
            Success = true,
            RefundId = refund.RefundId,
            Status = RefundStatus.Rejected.ToString(),
            Message = "Refund request rejected"
        };
    }

    /// <summary>
    /// Employee-initiated partial refund — goes directly to Paymob, no admin approval.
    /// If no invoice/payment found, notifies admins without creating a refund record.
    /// </summary>
    public async Task<RefundResponse> ProcessEmployeeRefundAsync(int orderId, decimal amount, string reason)
    {
        var order = await _db.Orders
            .Include(o => o.Invoices)
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

        if (order == null)
            return new RefundResponse { Success = false, Message = "Order not found" };

        var invoice = order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Paid);
        var payment = invoice != null
            ? await _db.Payments.FirstOrDefaultAsync(p => p.InvoiceId == invoice.InvoiceId && p.PaymentStatus == PaymentStatus.Completed)
            : null;

        // If no paid invoice or payment → notify admins (no refund record without payment)
        if (invoice == null || payment == null)
        {
            var failReason = invoice == null ? "No paid invoice found" : "No completed payment found";

            var adminIds = await _db.Admins
                .Where(a => a.IsActive)
                .Select(a => a.AdminId)
                .ToListAsync();

            foreach (var aid in adminIds)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = aid,
                    UserType = UserType.Admin,
                    NotificationType = NotificationType.OrderUpdated,
                    Title = "Refund Failed — Action Required",
                    Message = $"Refund of {amount:F2} EGP for order #{orderId} could not be processed. {failReason}. Please review manually.",
                    NotificationChannel = NotificationChannel.InApp,
                    OrderId = orderId
                });
            }

            await _db.SaveChangesAsync();

            return new RefundResponse { Success = false, Message = failReason };
        }

        // Validate refund amount
        if (amount > invoice.TotalAmount)
            return new RefundResponse { Success = false, Message = "Refund amount exceeds invoice total" };

        var refund = new Refund
        {
            RefundAmount = amount,
            RefundStatus = RefundStatus.Requested,
            Reason = reason,
            OrderId = orderId,
            PaymentId = payment.PaymentId,
            Order = order,
            Payment = payment
        };
        _db.Refunds.Add(refund);

        // Execute immediately through Paymob (partial refund)
        return await ExecutePaymobRefundAsync(
            refund,
            amount,
            adminId: null,
            title: "Customs fees refunded",
            message: $"Amount of {amount:F2} EGP has been refunded for customs fees",
            isPartial: true);
    }
}
