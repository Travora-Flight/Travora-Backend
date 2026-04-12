using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travora.Application.DTOs.Payments;
using Travora.Application.Interfaces.Services;
using Travora.Application.Interfaces.Services.Customer;
using Microsoft.Extensions.Logging;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Domain.Constants;
using Travora.Infrastructure.Data;
using Travora.Shared.Settings;

namespace Travora.Infrastructure.Services;

public class PaymobService : IPaymobService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PaymobSettings _settings;
    private readonly IDoorToDoorOrderService _doorToDoorOrderService;
    private readonly ICarServiceOrderService _carServiceOrderService;
    private readonly ICustomerOrderService _customerOrderService;
    private readonly ILogger<PaymobService> _logger;
    private readonly INotificationPusher _pusher;

    public PaymobService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<PaymobSettings> settings,
        IDoorToDoorOrderService doorToDoorOrderService,
        ICarServiceOrderService carServiceOrderService,
        ICustomerOrderService customerOrderService,
        ILogger<PaymobService> logger,
        INotificationPusher pusher)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _doorToDoorOrderService = doorToDoorOrderService;
        _carServiceOrderService = carServiceOrderService;
        _customerOrderService = customerOrderService;
        _logger = logger;
        _pusher = pusher;
    }

    public async Task<PaymentInitiationResponse> InitiatePaymentAsync(int orderId, int customerId)
    {
        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Invoices)
            .Include(o => o.Package)
            .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId)
            ?? throw new KeyNotFoundException("الأوردر مش موجود");

        if (order.OrderStatus != OrderStatus.Pending)
            throw new InvalidOperationException("الأوردر مش في حالة انتظار الدفع");

        // Allow re-payment after failed attempt — reset Failed invoice back to Pending
        var invoice = order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Pending)
            ?? order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Failed)
            ?? throw new InvalidOperationException("مفيش فاتورة للأوردر ده");

        if (invoice.InvoiceStatus == InvoiceStatus.Failed)
        {
            invoice.InvoiceStatus = InvoiceStatus.Pending;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        var customer = order.Customer;
        var amountCents = (int)(invoice.TotalAmount * 100);
        var client = _httpClientFactory.CreateClient("Paymob");

        // Step 1: Auth Token
        var authPayload = new { api_key = _settings.ApiKey };
        var authResponse = await client.PostAsJsonAsync($"{_settings.BaseUrl}/api/auth/tokens", authPayload);
        authResponse.EnsureSuccessStatusCode();
        var authResult = await authResponse.Content.ReadFromJsonAsync<JsonElement>();
        var authToken = authResult.GetProperty("token").GetString()!;

        var currency = order.Package?.Currency ?? "EGP";

        // Step 2: Order Registration
        var orderPayload = new
        {
            auth_token = authToken,
            delivery_needed = false,
            amount_cents = amountCents,
            currency = currency,
            merchant_order_id = orderId.ToString(),
            items = Array.Empty<object>()
        };
        var orderResponse = await client.PostAsJsonAsync($"{_settings.BaseUrl}/api/ecommerce/orders", orderPayload);
        orderResponse.EnsureSuccessStatusCode();
        var orderResult = await orderResponse.Content.ReadFromJsonAsync<JsonElement>();
        var paymobOrderId = orderResult.GetProperty("id").GetInt64();

        // Step 3: Payment Key
        var paymentKeyPayload = new
        {
            auth_token = authToken,
            amount_cents = amountCents,
            expiration = 3600,
            order_id = paymobOrderId,
            currency = currency,
            integration_id = _settings.IntegrationId,
            billing_data = new
            {
                first_name = customer.Firstname,
                last_name = customer.Lastname,
                email = customer.Email,
                phone_number = customer.PhoneNumber,
                apartment = "NA",
                floor = "NA",
                street = "NA",
                building = "NA",
                shipping_method = "NA",
                postal_code = "NA",
                city = "NA",
                country = "EG",
                state = "NA"
            }
        };
        var paymentKeyResponse = await client.PostAsJsonAsync($"{_settings.BaseUrl}/api/acceptance/payment_keys", paymentKeyPayload);
        paymentKeyResponse.EnsureSuccessStatusCode();
        var paymentKeyResult = await paymentKeyResponse.Content.ReadFromJsonAsync<JsonElement>();
        var paymentKey = paymentKeyResult.GetProperty("token").GetString()!;

        // Create new Payment record (old failed payments remain as-is for audit)
        var payment = new Payment
        {
            Amount = invoice.TotalAmount,
            Currency = currency,
            OrderIdFromGateway = paymobOrderId.ToString(),
            PaymentStatus = PaymentStatus.Pending,
            PaymentGateway = "Paymob",
            InvoiceId = invoice.InvoiceId,
            PaymentMethodId = await GetOrCreatePaymentMethodIdAsync(customerId)
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        var iframeUrl = $"{_settings.BaseUrl}/api/acceptance/iframes/{_settings.IframeId}?payment_token={paymentKey}";

        return new PaymentInitiationResponse
        {
            Success = true,
            PaymentKey = paymentKey,
            IframeUrl = iframeUrl,
            OrderId = orderId,
            Amount = invoice.TotalAmount
        };
    }

    public async Task HandleWebhookAsync(System.Text.Json.JsonElement payload, string hmacFromPaymob)
    {
        // 1. فلتر الاسترداد: تجاهل الريكويست لو كان يخص Refund
        var type = payload.TryGetProperty("type", out var t) ? t.GetString() : "";
        if (string.Equals(type, "REFUND", StringComparison.OrdinalIgnoreCase))
            return;

        if (!payload.TryGetProperty("obj", out var obj))
            return;

        // تأكيد إضافي لتجاهل عمليات الدفع اللي معمولها استرداد
        if (obj.TryGetProperty("is_refunded", out var isRef) && isRef.ValueKind == System.Text.Json.JsonValueKind.True)
            return;

        // 2. تجميع الـ HMAC بالترتيب الأبجدي الصارم واستخراج القيم خام من الـ JSON
        var amount_cents = obj.GetProperty("amount_cents").GetRawText();
        var created_at = obj.GetProperty("created_at").GetString();
        var currency = obj.GetProperty("currency").GetString();
        var error_occured = obj.GetProperty("error_occured").GetBoolean().ToString().ToLower();
        var has_parent_transaction = obj.GetProperty("has_parent_transaction").GetBoolean().ToString().ToLower();
        var id = obj.GetProperty("id").GetRawText();
        var integration_id = obj.GetProperty("integration_id").GetRawText();
        var is_3d_secure = obj.GetProperty("is_3d_secure").GetBoolean().ToString().ToLower();
        var is_auth = obj.GetProperty("is_auth").GetBoolean().ToString().ToLower();
        var is_capture = obj.GetProperty("is_capture").GetBoolean().ToString().ToLower();
        var is_refunded = obj.GetProperty("is_refunded").GetBoolean().ToString().ToLower();
        var is_standalone_payment = obj.GetProperty("is_standalone_payment").GetBoolean().ToString().ToLower();
        var is_voided = obj.GetProperty("is_voided").GetBoolean().ToString().ToLower();
        var order_id = obj.GetProperty("order").GetProperty("id").GetRawText();
        var owner = obj.GetProperty("owner").GetRawText();
        var pending = obj.GetProperty("pending").GetBoolean().ToString().ToLower();
        var source_data_pan = obj.GetProperty("source_data").GetProperty("pan").GetString() ?? "";
        var source_data_sub_type = obj.GetProperty("source_data").GetProperty("sub_type").GetString() ?? "";
        var source_data_type = obj.GetProperty("source_data").GetProperty("type").GetString() ?? "";
        var success_bool = obj.GetProperty("success").GetBoolean();
        var success = success_bool.ToString().ToLower();

        // دمج المتغيرات
        var concatenated = amount_cents + created_at + currency + error_occured + has_parent_transaction +
                        id + integration_id + is_3d_secure + is_auth + is_capture + is_refunded +
                        is_standalone_payment + is_voided + order_id + owner + pending +
                        source_data_pan + source_data_sub_type + source_data_type + success;

        // التشفير والمقارنة (الحماية شغالة ومفيش كومنت)
        var computedHmac = ComputeHmacSha512(concatenated, _settings.HmacSecret);

        if (!string.Equals(computedHmac, hmacFromPaymob, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("HMAC Mismatch! Computed: {Computed}, Received: {Received}", computedHmac, hmacFromPaymob);
            throw new UnauthorizedAccessException("Invalid HMAC signature");
        }

        // 3. استخراج باقي الداتا عشان نحدث الداتا بيز
        var merchantOrderIdStr = obj.GetProperty("order").GetProperty("merchant_order_id").GetString();
        var transactionId = id;
        var paymobOrderId = order_id;

        if (!int.TryParse(merchantOrderIdStr, out var orderId))
            return; // أوردر غير معروف

        var order = await _db.Orders.Include(o => o.Invoices).FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order == null) return;

        var invoice = order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Pending || i.InvoiceStatus == InvoiceStatus.Draft);
        if (invoice == null) return;

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.OrderIdFromGateway == paymobOrderId && p.InvoiceId == invoice.InvoiceId);
        var now = DateTime.UtcNow;

        if (success_bool)
        {
            // تم الدفع بنجاح
            invoice.InvoiceStatus = InvoiceStatus.Paid;
            invoice.PaidAt = now;
            invoice.UpdatedAt = now;

            order.OrderStatus = OrderStatus.Confirmed;
            order.UpdatedAt = now;

            if (payment != null)
            {
                payment.PaymentStatus = PaymentStatus.Completed;
                payment.TransactionId = transactionId;
                payment.UpdatedAt = now;

                // تحديث بيانات كارت الدفع
                var paymentMethod = await _db.PaymentMethods.FirstOrDefaultAsync(pm => pm.PaymentMethodId == payment.PaymentMethodId);
                if (paymentMethod != null)
                {
                    if (source_data_pan.Length >= 4) paymentMethod.CardLastFour = source_data_pan[^4..];
                    paymentMethod.CardBrand = source_data_type;
                    paymentMethod.PaymentFunding = source_data_sub_type.ToLower() switch
                    {
                        "debit" => PaymentFunding.Debit,
                        "prepaid" => PaymentFunding.Prepaid,
                        _ => PaymentFunding.Credit
                    };
                    paymentMethod.UpdatedAt = now;
                }
            }
        }
        else
        {
            // فشل الدفع
            invoice.InvoiceStatus = InvoiceStatus.Failed;
            invoice.UpdatedAt = now;

            if (payment != null)
            {
                payment.PaymentStatus = PaymentStatus.Failed;
                payment.TransactionId = transactionId;
                
                var gatewayMsg = "Payment failed";
                if (obj.TryGetProperty("data", out var dataObj) && dataObj.TryGetProperty("message", out var msgProp))
                    gatewayMsg = msgProp.GetString() ?? "Payment failed";
                    
                payment.GatewayResponse = gatewayMsg;
                payment.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync();

        // Customer notification for payment result
        if (success_bool)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = order.CustomerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.OrderUpdated,
                Title = "Payment successful",
                Message = $"Your payment of {invoice.TotalAmount} EGP for order #{orderId} has been received",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = orderId
            });
            await _db.SaveChangesAsync();

            await _pusher.PushToCustomerAsync(
                order.CustomerId,
                "Payment successful",
                $"Your payment of {invoice.TotalAmount} EGP for order #{orderId} has been received",
                "PaymentSuccess",
                orderId);
        }
        else
        {
            _db.Notifications.Add(new Notification
            {
                UserId = order.CustomerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.OrderUpdated,
                Title = "Payment failed",
                Message = $"Your payment for order #{orderId} was not successful. Please try again.",
                NotificationChannel = NotificationChannel.InApp,
                OrderId = orderId
            });
            await _db.SaveChangesAsync();

            await _pusher.PushToCustomerAsync(
                order.CustomerId,
                "Payment failed",
                $"Your payment for order #{orderId} was not successful. Please try again.",
                "PaymentFailed",
                orderId);
        }

        // 4. استكمال الخدمات وتعيين الموظفين بعد الدفع
        if (success_bool)
        {
            var orderWithPackage = await _db.Orders.Include(o => o.Package).FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (orderWithPackage?.Package?.PackageName == PackageNames.DoorToDoor)
                await _doorToDoorOrderService.AssignEmployeesAfterPaymentAsync(orderId);
            else if (orderWithPackage?.Package?.PackageName == PackageNames.CarServiceToAirport || orderWithPackage?.Package?.PackageName == PackageNames.CarServiceFromAirport)
                await _carServiceOrderService.AssignEmployeesAfterPaymentAsync(orderId);
            else if (orderWithPackage?.Package?.PackageName == PackageNames.TrackingBaggage)
            {
                var orderService = await _db.OrderServices.FirstOrDefaultAsync(os => os.OrderId == orderId);
                if (orderService != null)
                {
                    orderService.ServiceStatus = ServiceStatus.InProgress;
                    orderService.ActualStartTime = DateTime.UtcNow;
                    orderService.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            // إنشاء البوردينج باس
            if (orderWithPackage?.Package?.PackageName is PackageNames.DoorToDoor or PackageNames.CarServiceToAirport)
            {
                _ = Task.Run(async () =>
                {
                    try { await _customerOrderService.GenerateBoardingPassesAsync(orderId); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to auto-generate boarding passes for order {OrderId}", orderId); }
                });
            }
        }
    }
    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Invoices)
            .FirstOrDefaultAsync(o => o.OrderId == orderId)
            ?? throw new KeyNotFoundException("الأوردر مش موجود");

        var invoice = order.Invoices
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefault();

        return new PaymentStatusResponse
        {
            OrderId = orderId,
            OrderStatus = order.OrderStatus.ToString(),
            InvoiceStatus = invoice?.InvoiceStatus.ToString() ?? "N/A",
            Amount = invoice?.TotalAmount ?? 0,
            PaidAt = invoice?.PaidAt
        };
    }

    // ===== Helpers =====

    private static string ComputeHmacSha512(string data, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        using var hmac = new HMACSHA512(keyBytes);
        var hashBytes = hmac.ComputeHash(dataBytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    private async Task<int> GetOrCreatePaymentMethodIdAsync(int customerId)
    {
        var existing = await _db.PaymentMethods
            .FirstOrDefaultAsync(pm => pm.CustomerId == customerId && pm.IsActive && pm.PaymentFunding == PaymentFunding.Credit);

        if (existing != null)
            return existing.PaymentMethodId;

        var paymentMethod = new PaymentMethod
        {
            CustomerId = customerId,
            PaymentFunding = PaymentFunding.Credit,
            CardLastFour = "0000",
            CardHolderName = "Paymob",
            CardBrand = "Paymob",
            IsDefault = true,
            IsActive = true
        };
        _db.PaymentMethods.Add(paymentMethod);
        await _db.SaveChangesAsync();
        return paymentMethod.PaymentMethodId;
    }
}
