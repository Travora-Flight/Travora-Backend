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

    // Diagnostic: stores last webhook payloads for debugging (remove in production)
    private static readonly List<object> _lastWebhooks = new();
    private static readonly object _lock = new();
    public static IReadOnlyList<object> LastWebhooks { get { lock (_lock) { return _lastWebhooks.ToList(); } } }

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

    public async Task<PaymentInitiationResponse> InitiatePaymentAsync(int orderId, int customerId, int? paymentMethodId = null)
    {
        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Invoices)
            .Include(o => o.Package)
            .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId)
            ?? throw new KeyNotFoundException("Order not found");

        if (order.OrderStatus != OrderStatus.Pending)
            throw new InvalidOperationException("Order is not in pending payment status");

        // Allow re-payment after failed attempt — reset Failed invoice back to Pending
        var invoice = order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Pending)
            ?? order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Failed)
            ?? throw new InvalidOperationException("No invoice found for this order");

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

        // Token direct payment (CIT / One-Click Checkout)
        if (paymentMethodId.HasValue)
        {
            var savedMethod = await _db.PaymentMethods
                .FirstOrDefaultAsync(pm => pm.PaymentMethodId == paymentMethodId.Value && pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted);

            if (savedMethod != null && !string.IsNullOrEmpty(savedMethod.PaymobCardToken))
            {
                var directChargePayload = new
                {
                    source = new
                    {
                        identifier = savedMethod.PaymobCardToken,
                        subtype = "TOKEN"
                    },
                    payment_token = paymentKey
                };

                var chargeResponse = await client.PostAsJsonAsync($"{_settings.BaseUrl}/api/acceptance/payments/pay", directChargePayload);
                if (chargeResponse.IsSuccessStatusCode)
                {
                    var chargeResult = await chargeResponse.Content.ReadFromJsonAsync<JsonElement>();
                    
                    var success = false;
                    if (chargeResult.TryGetProperty("success", out var successProp) && successProp.ValueKind == JsonValueKind.True)
                    {
                        success = true;
                    }
                    else if (chargeResult.TryGetProperty("success", out var successPropStr) && successPropStr.ValueKind == JsonValueKind.String && successPropStr.GetString() == "true")
                    {
                        success = true;
                    }

                    var transactionId = chargeResult.TryGetProperty("id", out var idProp) ? idProp.GetRawText() : "";

                    if (success)
                    {
                        var now = DateTime.UtcNow;

                        // Create completed Payment record
                        var completedPayment = new Payment
                        {
                            Amount = invoice.TotalAmount,
                            Currency = currency,
                            OrderIdFromGateway = paymobOrderId.ToString(),
                            PaymentStatus = PaymentStatus.Completed,
                            PaymentGateway = "Paymob",
                            TransactionId = transactionId,
                            InvoiceId = invoice.InvoiceId,
                            PaymentMethodId = savedMethod.PaymentMethodId,
                            PaymentDate = now,
                            CreatedAt = now
                        };
                        _db.Payments.Add(completedPayment);

                        // Update invoice & order
                        invoice.InvoiceStatus = InvoiceStatus.Paid;
                        invoice.PaidAt = now;
                        invoice.UpdatedAt = now;

                        order.OrderStatus = OrderStatus.Confirmed;
                        order.UpdatedAt = now;

                        await _db.SaveChangesAsync();

                        // Dispatch employee assignments and triggers
                        if (order.Package?.PackageName == PackageNames.DoorToDoor)
                            await _doorToDoorOrderService.AssignEmployeesAfterPaymentAsync(orderId);
                        else if (order.Package?.PackageName == PackageNames.CarServiceToAirport || order.Package?.PackageName == PackageNames.CarServiceFromAirport)
                            await _carServiceOrderService.AssignEmployeesAfterPaymentAsync(orderId);
                        else if (order.Package?.PackageName == PackageNames.TrackingBaggage)
                        {
                            var orderService = await _db.OrderServices.FirstOrDefaultAsync(os => os.OrderId == orderId);
                            if (orderService != null)
                            {
                                orderService.ServiceStatus = ServiceStatus.InProgress;
                                orderService.ActualStartTime = now;
                                orderService.UpdatedAt = now;
                                await _db.SaveChangesAsync();
                            }
                        }

                        // Generate boarding pass
                        if (order.Package?.PackageName is PackageNames.DoorToDoor or PackageNames.CarServiceToAirport)
                        {
                            _ = Task.Run(async () =>
                            {
                                try { await _customerOrderService.GenerateBoardingPassesAsync(orderId); }
                                catch (Exception ex) { _logger.LogError(ex, "Failed to auto-generate boarding passes for order {OrderId}", orderId); }
                            });
                        }

                        // Push Notification
                        _db.Notifications.Add(new Notification
                        {
                            UserId = customerId,
                            UserType = UserType.Customer,
                            NotificationType = NotificationType.OrderUpdated,
                            Title = "Payment successful",
                            Message = $"Your payment of {invoice.TotalAmount} EGP for order #{orderId} has been successfully processed using your saved card.",
                            NotificationChannel = NotificationChannel.InApp,
                            OrderId = orderId
                        });
                        await _db.SaveChangesAsync();

                        await _pusher.PushToCustomerAsync(
                            customerId,
                            "Payment successful",
                            $"Your payment of {invoice.TotalAmount} EGP for order #{orderId} has been successfully processed using your saved card.",
                            "PaymentSuccess",
                            orderId);

                        // Return Success directly without iframe!
                        return new PaymentInitiationResponse
                        {
                            Success = true,
                            PaymentKey = paymentKey,
                            IframeUrl = string.Empty, // Empty indicates direct payment completed!
                            OrderId = orderId,
                            Amount = invoice.TotalAmount
                        };
                    }
                    else
                    {
                        var failMsg = "Token charge was rejected by the gateway";
                        if (chargeResult.TryGetProperty("data", out var dataObj) && dataObj.TryGetProperty("message", out var msgProp))
                        {
                            failMsg = msgProp.GetString() ?? failMsg;
                        }

                        return new PaymentInitiationResponse
                        {
                            Success = false,
                            ErrorMessage = failMsg,
                            OrderId = orderId,
                            Amount = invoice.TotalAmount
                        };
                    }
                }
                else
                {
                    return new PaymentInitiationResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to charge saved card. Direct payment gateway call failed.",
                        OrderId = orderId,
                        Amount = invoice.TotalAmount
                    };
                }
            }
        }

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

    public async Task<PaymentInitiationResponse> InitiateSaveCardAsync(int customerId)
    {
        var customer = await _db.Customers.FindAsync(customerId)
            ?? throw new KeyNotFoundException("Customer not found");

        var client = _httpClientFactory.CreateClient("Paymob");

        try
        {
            // Step 1: Auth Token
            var authPayload = new { api_key = _settings.ApiKey };
            var authResponse = await client.PostAsJsonAsync($"{_settings.BaseUrl}/api/auth/tokens", authPayload);
            var authContent = await authResponse.Content.ReadAsStringAsync();
            if (!authResponse.IsSuccessStatusCode)
            {
                _logger.LogError("SaveCard Step1 Auth failed: {StatusCode} - {Body}", authResponse.StatusCode, authContent);
                return new PaymentInitiationResponse { Success = false, ErrorMessage = $"Paymob auth failed: {authResponse.StatusCode}" };
            }
            var authResult = JsonSerializer.Deserialize<JsonElement>(authContent);
            var authToken = authResult.GetProperty("token").GetString()!;

            // Step 2: Register Order with 100 cents (verification amount)
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var orderPayload = new
            {
                auth_token = authToken,
                delivery_needed = false,
                amount_cents = 100,
                currency = "EGP",
                merchant_order_id = $"card_save_{customerId}_{timestamp}",
                items = Array.Empty<object>()
            };
            var orderResponse = await client.PostAsJsonAsync($"{_settings.BaseUrl}/api/ecommerce/orders", orderPayload);
            var orderContent = await orderResponse.Content.ReadAsStringAsync();
            if (!orderResponse.IsSuccessStatusCode)
            {
                _logger.LogError("SaveCard Step2 Order failed: {StatusCode} - {Body}", orderResponse.StatusCode, orderContent);
                return new PaymentInitiationResponse { Success = false, ErrorMessage = $"Paymob order registration failed: {orderResponse.StatusCode}" };
            }
            var orderResult = JsonSerializer.Deserialize<JsonElement>(orderContent);
            var paymobOrderId = orderResult.GetProperty("id").GetInt64();

            // Step 3: Generate Payment Key
            var paymentKeyPayload = new
            {
                auth_token = authToken,
                amount_cents = 100,
                expiration = 3600,
                order_id = paymobOrderId,
                currency = "EGP",
                integration_id = _settings.IntegrationId,
                billing_data = new
                {
                    first_name = string.IsNullOrEmpty(customer.Firstname) ? "Customer" : customer.Firstname,
                    last_name = string.IsNullOrEmpty(customer.Lastname) ? "User" : customer.Lastname,
                    email = string.IsNullOrEmpty(customer.Email) ? "customer@travora.com" : customer.Email,
                    phone_number = string.IsNullOrEmpty(customer.PhoneNumber) ? "01000000000" : customer.PhoneNumber,
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
            var paymentKeyContent = await paymentKeyResponse.Content.ReadAsStringAsync();
            if (!paymentKeyResponse.IsSuccessStatusCode)
            {
                _logger.LogError("SaveCard Step3 PaymentKey failed: {StatusCode} - {Body}", paymentKeyResponse.StatusCode, paymentKeyContent);
                return new PaymentInitiationResponse { Success = false, ErrorMessage = $"Paymob payment key failed: {paymentKeyResponse.StatusCode}" };
            }
            var paymentKeyResult = JsonSerializer.Deserialize<JsonElement>(paymentKeyContent);
            var paymentKey = paymentKeyResult.GetProperty("token").GetString()!;

            var iframeUrl = $"{_settings.BaseUrl}/api/acceptance/iframes/{_settings.IframeId}?payment_token={paymentKey}";

            return new PaymentInitiationResponse
            {
                Success = true,
                PaymentKey = paymentKey,
                IframeUrl = iframeUrl,
                OrderId = 0,
                Amount = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InitiateSaveCardAsync failed for customer {CustomerId}", customerId);
            return new PaymentInitiationResponse { Success = false, ErrorMessage = $"Card save initiation failed: {ex.Message}" };
        }
    }

    public async Task HandleWebhookAsync(System.Text.Json.JsonElement payload, string hmacFromPaymob)
    {
        _logger.LogInformation("Webhook received. HMAC length: {HmacLength}", hmacFromPaymob?.Length ?? 0);

        // Diagnostic: store the webhook payload
        lock (_lock)
        {
            _lastWebhooks.Add(new
            {
                ReceivedAt = DateTime.UtcNow,
                Hmac = hmacFromPaymob,
                Payload = payload.ToString()
            });
            // Keep only last 5 webhooks
            while (_lastWebhooks.Count > 5) _lastWebhooks.RemoveAt(0);
        }

        // 1. Refund Filter: ignore request if it concerns Refund
        var type = payload.TryGetProperty("type", out var t) ? t.GetString() : "";
        if (string.Equals(type, "REFUND", StringComparison.OrdinalIgnoreCase))
            return;

        if (!payload.TryGetProperty("obj", out var obj))
            return;

        // Additional confirmation to ignore payments that have been refunded
        if (obj.TryGetProperty("is_refunded", out var isRef) && isRef.ValueKind == System.Text.Json.JsonValueKind.True)
            return;

        // 2. Assemble HMAC in strict alphabetical order and extract raw values from JSON
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

        // Concatenate variables
        var concatenated = amount_cents + created_at + currency + error_occured + has_parent_transaction +
                        id + integration_id + is_3d_secure + is_auth + is_capture + is_refunded +
                        is_standalone_payment + is_voided + order_id + owner + pending +
                        source_data_pan + source_data_sub_type + source_data_type + success;

        // Encryption and comparison
        var computedHmac = ComputeHmacSha512(concatenated, _settings.HmacSecret);

        if (!string.Equals(computedHmac, hmacFromPaymob, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("HMAC Mismatch! Computed: {Computed}, Received: {Received}", computedHmac, hmacFromPaymob);
            throw new UnauthorizedAccessException("Invalid HMAC signature");
        }

        // 3. Extract remaining data to update the database
        var merchantOrderIdStr = obj.GetProperty("order").GetProperty("merchant_order_id").GetString() ?? "";
        var transactionId = id;
        var paymobOrderId = order_id;

        // Try to get token
        string? cardToken = null;
        if (obj.GetProperty("source_data").TryGetProperty("token", out var tokenProp))
        {
            cardToken = tokenProp.GetString();
        }

        // Handle Card Saving Hook (Zero amount checkout / Add card beforehand)
        if (merchantOrderIdStr.StartsWith("card_save_"))
        {
            _logger.LogInformation("Card save webhook received: merchant_order_id={MerchantOrderId}, success={Success}, token={Token}, pan={Pan}",
                merchantOrderIdStr, success_bool, cardToken ?? "NULL", source_data_pan);

            var parts = merchantOrderIdStr.Split('_');
            if (parts.Length >= 3 && int.TryParse(parts[2], out var customerId))
            {
                if (success_bool)
                {
                    // Check for duplicate: if token exists, check by token; otherwise check by pan
                    bool exists;
                    if (!string.IsNullOrEmpty(cardToken))
                    {
                        exists = await _db.PaymentMethods.AnyAsync(pm => pm.CustomerId == customerId && pm.PaymobCardToken == cardToken && pm.IsActive && !pm.IsDeleted);
                    }
                    else
                    {
                        var lastFour = source_data_pan.Length >= 4 ? source_data_pan[^4..] : "0000";
                        exists = await _db.PaymentMethods.AnyAsync(pm => pm.CustomerId == customerId && pm.CardLastFour == lastFour && pm.CardBrand == source_data_type && pm.IsActive && !pm.IsDeleted);
                    }

                    if (!exists)
                    {
                        var nowUtc = DateTime.UtcNow;
                        var hasCards = await _db.PaymentMethods.AnyAsync(pm => pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted);

                        var paymentMethod = new PaymentMethod
                        {
                            CustomerId = customerId,
                            CardLastFour = source_data_pan.Length >= 4 ? source_data_pan[^4..] : "0000",
                            CardBrand = source_data_type,
                            CardHolderName = "Saved Card",
                            PaymentFunding = source_data_sub_type.ToLower() switch
                            {
                                "debit" => PaymentFunding.Debit,
                                "prepaid" => PaymentFunding.Prepaid,
                                _ => PaymentFunding.Credit
                            },
                            PaymobCardToken = cardToken, // Will be null if tokenization not enabled yet
                            IsDefault = !hasCards,
                            IsActive = true,
                            IsDeleted = false,
                            AddedAt = nowUtc,
                            CreatedAt = nowUtc
                        };
                        _db.PaymentMethods.Add(paymentMethod);
                        await _db.SaveChangesAsync();

                        _logger.LogInformation("Card saved for customer {CustomerId}: last4={Last4}, brand={Brand}, hasToken={HasToken}",
                            customerId, paymentMethod.CardLastFour, paymentMethod.CardBrand, !string.IsNullOrEmpty(cardToken));

                        // Notification
                        _db.Notifications.Add(new Notification
                        {
                            UserId = customerId,
                            UserType = UserType.Customer,
                            NotificationType = NotificationType.OrderUpdated,
                            Title = "Card saved successfully",
                            Message = $"Your card ending in {paymentMethod.CardLastFour} has been successfully added to your profile.",
                            NotificationChannel = NotificationChannel.InApp,
                            OrderId = 0
                        });
                        await _db.SaveChangesAsync();

                        await _pusher.PushToCustomerAsync(
                            customerId,
                            "Card saved successfully",
                            $"Your card ending in {paymentMethod.CardLastFour} has been successfully added to your profile.",
                            "CardSaved",
                            0);
                    }
                    else
                    {
                        _logger.LogInformation("Card already exists for customer {CustomerId}, skipping save.", customerId);
                    }
                }
                else
                {
                    _logger.LogWarning("Card save webhook received with success=false for customer {CustomerId}", customerId);
                }
            }
            return;
        }

        if (!int.TryParse(merchantOrderIdStr, out var orderId))
            return; // Unknown order

        var order = await _db.Orders.Include(o => o.Invoices).FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order == null) return;

        var invoice = order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Pending || i.InvoiceStatus == InvoiceStatus.Draft);
        if (invoice == null) return;

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.OrderIdFromGateway == paymobOrderId && p.InvoiceId == invoice.InvoiceId);
        var now = DateTime.UtcNow;

        if (success_bool)
        {
            // Payment successful
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

                // Update payment card data
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
                    if (!string.IsNullOrEmpty(cardToken))
                    {
                        paymentMethod.PaymobCardToken = cardToken;
                    }
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

        // 4. Complete services and assign employees after payment
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

            // Generate boarding pass
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
            ?? throw new KeyNotFoundException("Order not found");

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
