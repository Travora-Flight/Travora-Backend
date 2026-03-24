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

    public PaymobService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<PaymobSettings> settings,
        IDoorToDoorOrderService doorToDoorOrderService,
        ICarServiceOrderService carServiceOrderService,
        ICustomerOrderService customerOrderService,
        ILogger<PaymobService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _doorToDoorOrderService = doorToDoorOrderService;
        _carServiceOrderService = carServiceOrderService;
        _customerOrderService = customerOrderService;
        _logger = logger;
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

    public async Task HandleWebhookAsync(Dictionary<string, string> formData, string hmacFromPaymob)
    {
        var isRefund = formData.GetValueOrDefault("is_refund", "false");
        if (string.Equals(isRefund, "true", StringComparison.OrdinalIgnoreCase))
            return;

        // Verify HMAC
        var hmacFields = new[]
        {
            "amount_cents", "created_at", "currency", "error_occured",
            "has_parent_transaction", "id", "integration_id", "is_3d_secure",
            "is_auth", "is_capture", "is_refunded", "is_standalone_payment",
            "is_voided", "order.id", "owner", "pending", "source_data.pan",
            "source_data.sub_type", "source_data.type", "success"
        };

        var concatenated = string.Join("", hmacFields.Select(f => formData.GetValueOrDefault(f, "")));
        var computedHmac = ComputeHmacSha512(concatenated, _settings.HmacSecret);

        // if (!string.Equals(computedHmac, hmacFromPaymob, StringComparison.OrdinalIgnoreCase))
        //     throw new UnauthorizedAccessException("Invalid HMAC signature");

        // Extract data
        var merchantOrderId = formData.GetValueOrDefault("order.merchant_order_id", "");
        var success = formData.GetValueOrDefault("success", "false");
        var transactionId = formData.GetValueOrDefault("id", "");
        var paymobOrderId = formData.GetValueOrDefault("order.id", "");

        if (!int.TryParse(merchantOrderId, out var orderId))
            return; // Unknown order — return 200 silently

        var order = await _db.Orders
            .Include(o => o.Invoices)
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

        if (order == null)
            return; // Order not found — return 200 silently

        var invoice = order.Invoices.FirstOrDefault(i =>
            i.InvoiceStatus == InvoiceStatus.Pending || i.InvoiceStatus == InvoiceStatus.Draft);

        if (invoice == null)
            return;

        // Find payment by gateway order ID
        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.OrderIdFromGateway == paymobOrderId && p.InvoiceId == invoice.InvoiceId);

        var now = DateTime.UtcNow;

        if (string.Equals(success, "true", StringComparison.OrdinalIgnoreCase))
        {
            // Payment succeeded
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

                // Update PaymentMethod with real card data
                var paymentMethod = await _db.PaymentMethods
                    .FirstOrDefaultAsync(pm => pm.PaymentMethodId == payment.PaymentMethodId);
                if (paymentMethod != null)
                {
                    var pan = formData.GetValueOrDefault("source_data.pan", "");
                    var cardBrand = formData.GetValueOrDefault("source_data.type", "");
                    var subType = formData.GetValueOrDefault("source_data.sub_type", "");

                    if (pan.Length >= 4)
                        paymentMethod.CardLastFour = pan[^4..];

                    if (!string.IsNullOrEmpty(cardBrand))
                        paymentMethod.CardBrand = cardBrand;

                    paymentMethod.PaymentFunding = subType?.ToLower() switch
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
            // Payment failed
            invoice.InvoiceStatus = InvoiceStatus.Failed;
            invoice.UpdatedAt = now;

            if (payment != null)
            {
                payment.PaymentStatus = PaymentStatus.Failed;
                payment.TransactionId = transactionId;
                payment.GatewayResponse = formData.GetValueOrDefault("data.message", "Payment failed");
                payment.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync();

        // Dispatch employee assignment AFTER SaveChanges so DB state is committed
        if (string.Equals(success, "true", StringComparison.OrdinalIgnoreCase))
        {
            var orderWithPackage = await _db.Orders
                .Include(o => o.Package)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (orderWithPackage?.Package?.PackageName == PackageNames.DoorToDoor)
                await _doorToDoorOrderService.AssignEmployeesAfterPaymentAsync(orderId);
            else if (orderWithPackage?.Package?.PackageName == PackageNames.CarServiceToAirport ||
                     orderWithPackage?.Package?.PackageName == PackageNames.CarServiceFromAirport)
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

            // Auto-generate boarding passes for eligible packages (fire and forget)
            if (orderWithPackage?.Package?.PackageName is PackageNames.DoorToDoor or PackageNames.CarServiceToAirport)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _customerOrderService.GenerateBoardingPassesAsync(orderId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to auto-generate boarding passes for order {OrderId}", orderId);
                    }
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
