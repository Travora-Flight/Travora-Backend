using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;

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
        INotificationPusher pusher,
        IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _doorToDoorOrderService = doorToDoorOrderService;
        _carServiceOrderService = carServiceOrderService;
        _customerOrderService = customerOrderService;
        _logger = logger;
        _pusher = pusher;
        _scopeFactory = scopeFactory;
    }

    // ==================== Helpers ====================

    /// <summary>
    /// Safely gets a boolean value from a JsonElement property that may be a real boolean or a string.
    /// </summary>
    private static bool GetBoolValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return false;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => prop.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            _ => false
        };
    }

    /// <summary>
    /// Refunds a transaction via Paymob API. Used to auto-refund card-save verification charges.
    /// </summary>
    private async Task RefundTransactionAsync(string transactionId, int amountCents)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Paymob");

            // Step 1: Authenticate to get auth_token (required for refund API)
            var authResponse = await client.PostAsJsonAsync(
                $"{_settings.BaseUrl}/api/auth/tokens",
                new { api_key = _settings.ApiKey });
            var authContent = await authResponse.Content.ReadAsStringAsync();

            if (!authResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Paymob auth for refund failed: {Status} - {Body}", authResponse.StatusCode, authContent);
                return;
            }

            var authToken = JsonSerializer.Deserialize<JsonElement>(authContent).GetProperty("token").GetString();

            // Step 2: Refund the transaction
            var refundResponse = await client.PostAsJsonAsync(
                $"{_settings.BaseUrl}/api/acceptance/void_refund/refund",
                new { auth_token = authToken, transaction_id = transactionId, amount_cents = amountCents });
            var refundContent = await refundResponse.Content.ReadAsStringAsync();

            if (refundResponse.IsSuccessStatusCode)
                _logger.LogInformation("Auto-refund successful for transaction {TxId}, amount={Amount} cents", transactionId, amountCents);
            else
                _logger.LogWarning("Auto-refund failed for transaction {TxId}: {Status} - {Body}", transactionId, refundResponse.StatusCode, refundContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-refund exception for transaction {TxId}", transactionId);
        }
    }
    // ==================== Intention API ====================

    /// <summary>
    /// Creates a Paymob payment intention via the Intention API.
    /// Single endpoint replaces the old 3-step flow (auth → order → payment key).
    /// </summary>
    private async Task<JsonElement?> CreateIntentionAsync(
        int amountCents, string currency, string specialReference,
        string firstName, string lastName, string email, string phone,
        List<string>? cardTokens = null, bool saveCard = false)
    {
        var client = _httpClientFactory.CreateClient("Paymob");
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Token {_settings.SecretKey}");

        var body = new Dictionary<string, object>
        {
            ["amount"] = amountCents,
            ["currency"] = currency,
            ["payment_methods"] = new object[] { _settings.IntegrationId },
            ["billing_data"] = new Dictionary<string, string>
            {
                ["first_name"] = string.IsNullOrEmpty(firstName) ? "Customer" : firstName,
                ["last_name"] = string.IsNullOrEmpty(lastName) ? "User" : lastName,
                ["email"] = string.IsNullOrEmpty(email) ? "customer@travora.com" : email,
                ["phone_number"] = string.IsNullOrEmpty(phone) ? "01000000000" : phone
            },
            ["special_reference"] = specialReference,
            ["notification_url"] = _settings.NotificationUrl
        };

        if (saveCard)
            body["save_card"] = true;

        if (cardTokens != null && cardTokens.Count > 0)
            body["card_tokens"] = cardTokens;

        var url = $"{_settings.BaseUrl}/v1/intention/";
        _logger.LogInformation("Creating intention: URL={Url}, Amount={Amount}, Currency={Currency}, Ref={Ref}, SaveCard={SaveCard}",
            url, amountCents, currency, specialReference, saveCard);

        var response = await client.PostAsJsonAsync(url, body);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Intention API failed: {Status} - {Body}", response.StatusCode, content);
            return null;
        }

        return JsonSerializer.Deserialize<JsonElement>(content);
    }

    /// <summary>
    /// Same as CreateIntentionAsync but returns the error detail string on failure.
    /// </summary>
    private async Task<(JsonElement? result, string? error)> CreateIntentionWithErrorAsync(
        int amountCents, string currency, string specialReference,
        string firstName, string lastName, string email, string phone,
        List<string>? cardTokens = null, bool saveCard = false)
    {
        var client = _httpClientFactory.CreateClient("Paymob");
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Token {_settings.SecretKey}");

        var body = new Dictionary<string, object>
        {
            ["amount"] = amountCents,
            ["currency"] = currency,
            ["payment_methods"] = new object[] { _settings.IntegrationId },
            ["billing_data"] = new Dictionary<string, string>
            {
                ["first_name"] = string.IsNullOrEmpty(firstName) ? "Customer" : firstName,
                ["last_name"] = string.IsNullOrEmpty(lastName) ? "User" : lastName,
                ["email"] = string.IsNullOrEmpty(email) ? "customer@travora.com" : email,
                ["phone_number"] = string.IsNullOrEmpty(phone) ? "01000000000" : phone
            },
            ["special_reference"] = specialReference,
            ["notification_url"] = _settings.NotificationUrl
        };

        if (saveCard)
            body["save_card"] = true;

        if (cardTokens != null && cardTokens.Count > 0)
            body["card_tokens"] = cardTokens;

        var url = $"{_settings.BaseUrl}/v1/intention/";
        _logger.LogInformation("Creating intention (v2): URL={Url}, Amount={Amount}, Ref={Ref}", url, amountCents, specialReference);

        var response = await client.PostAsJsonAsync(url, body);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Intention API failed: {Status} - {Body}", response.StatusCode, content);
            return (null, $"HTTP {response.StatusCode}: {content}");
        }

        return (JsonSerializer.Deserialize<JsonElement>(content), null);
    }

    /// <summary>
    /// Charges a saved card token directly via Paymob Pay API.
    /// Returns (redirectUrl, rawResponse):
    ///   - redirectUrl = non-empty string → 3DS redirect needed
    ///   - redirectUrl = "" → payment completed directly
    ///   - redirectUrl = null → payment failed (fallback to iframe)
    /// </summary>
    private async Task<(string? redirectUrl, string rawResponse)> PayWithSavedTokenAsync(string paymentKey, string cardToken)
    {
        var client = _httpClientFactory.CreateClient("Paymob");
        var body = new
        {
            source = new
            {
                identifier = cardToken,
                subtype = "TOKEN"
            },
            payment_token = paymentKey
        };

        var url = $"{_settings.BaseUrl}/api/acceptance/payments/pay";
        _logger.LogInformation("Pay-with-token: URL={Url}, Token={Token}", url, cardToken[..8] + "...");

        var content = "";
        try
        {
            var response = await client.PostAsJsonAsync(url, body);
            content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Pay-with-token response: {Status} - {Body}", response.StatusCode, content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Pay-with-token HTTP error: {Status} - {Body}", response.StatusCode, content);
                return (null, $"HTTP {response.StatusCode}: {content}");
            }

            var result = JsonSerializer.Deserialize<JsonElement>(content);

            // 1) Check for 3DS redirect URL (Paymob uses different field names)
            string? redirect = null;
            if (result.TryGetProperty("redirection_url", out var rdProp))
                redirect = rdProp.GetString();
            if (string.IsNullOrEmpty(redirect) && result.TryGetProperty("redirect_url", out var rProp))
                redirect = rProp.GetString();
            if (string.IsNullOrEmpty(redirect) && result.TryGetProperty("iframe_redir_url", out var iProp))
                redirect = iProp.GetString();

            if (!string.IsNullOrEmpty(redirect))
            {
                _logger.LogInformation("3DS redirect required: {Url}", redirect);
                return (redirect, content);
            }

            // 2) Check if the payment actually succeeded
            // Paymob may return booleans as strings ("true") or real booleans (true)
            bool success = GetBoolValue(result, "success");
            bool pending = GetBoolValue(result, "pending");

            if (success)
            {
                _logger.LogInformation("Pay-with-token completed directly (no 3DS)");
                return ("", content);
            }

            // 3) Payment did NOT succeed — return failure with raw response for debugging
            _logger.LogWarning("Pay-with-token: success={Success}, pending={Pending}. Response: {Body}", success, pending, content);
            return (null, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pay-with-token exception");
            return (null, $"Exception: {ex.Message} | Raw: {content}");
        }
    }

    // ==================== Payment Initiation ====================

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

        // Allow re-payment after failed attempt
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
        var currency = order.Package?.Currency ?? "EGP";

        // Check for saved card token (CIT flow)
        List<string>? cardTokens = null;
        if (paymentMethodId.HasValue)
        {
            var savedMethod = await _db.PaymentMethods
                .FirstOrDefaultAsync(pm => pm.PaymentMethodId == paymentMethodId.Value
                    && pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted);

            if (savedMethod?.PaymobCardToken != null)
                cardTokens = new List<string> { savedMethod.PaymobCardToken };
        }

        // Create Intention (single API call replaces old 3-step flow)
        // Append timestamp to special_reference to ensure uniqueness on retries
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var specialRef = $"{orderId}_{timestamp}";
        var result = await CreateIntentionAsync(
            amountCents, currency, specialRef,
            customer.Firstname, customer.Lastname, customer.Email, customer.PhoneNumber,
            cardTokens);

        if (result == null)
            return new PaymentInitiationResponse { Success = false, ErrorMessage = "Failed to create payment intention", OrderId = orderId, Amount = invoice.TotalAmount };

        var intentionData = result.Value;
        var clientSecret = intentionData.GetProperty("client_secret").GetString()!;
        var intentionOrderId = intentionData.GetProperty("intention_order_id").GetRawText();

        // Extract payment key from the response
        var paymentKey = "";
        if (intentionData.TryGetProperty("payment_keys", out var keys) && keys.GetArrayLength() > 0)
            paymentKey = keys[0].GetProperty("key").GetString() ?? "";

        // Create Payment record
        // PaymentMethodId is only set when the customer explicitly chooses a saved card.
        // One-time payments do NOT create or reference a PaymentMethod record.
        var payment = new Payment
        {
            Amount = invoice.TotalAmount,
            Currency = currency,
            OrderIdFromGateway = intentionOrderId,
            PaymentStatus = PaymentStatus.Pending,
            PaymentGateway = "Paymob",
            InvoiceId = invoice.InvoiceId,
            PaymentMethodId = paymentMethodId
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        // If paying with saved card token, charge directly via Pay API
        if (cardTokens != null && cardTokens.Count > 0 && !string.IsNullOrEmpty(paymentKey))
        {
            var (payRedirectUrl, payApiResponse) = await PayWithSavedTokenAsync(paymentKey, cardTokens[0]);

            if (payRedirectUrl != null) // null = failed, fallback to iframe
            {
                return new PaymentInitiationResponse
                {
                    Success = true,
                    ClientSecret = clientSecret,
                    PaymentKey = paymentKey,
                    IframeUrl = string.Empty,
                    RedirectUrl = string.IsNullOrEmpty(payRedirectUrl) ? null : payRedirectUrl,
                    OrderId = orderId,
                    Amount = invoice.TotalAmount,
                    ErrorMessage = null
                };
            }
            _logger.LogWarning("Pay-with-token failed for order {OrderId}, falling back to iframe. PayAPI: {Response}", orderId, payApiResponse);

            // Return iframe as fallback BUT include the Pay API response for debugging
            return new PaymentInitiationResponse
            {
                Success = true,
                ClientSecret = clientSecret,
                PaymentKey = paymentKey,
                IframeUrl = $"{_settings.BaseUrl}/api/acceptance/iframes/{_settings.IframeId}?payment_token={paymentKey}",
                OrderId = orderId,
                Amount = invoice.TotalAmount,
                ErrorMessage = $"[PayAPI-Debug] {payApiResponse}"
            };
        }

        // Normal flow: new card payment via iframe
        return new PaymentInitiationResponse
        {
            Success = true,
            ClientSecret = clientSecret,
            PaymentKey = paymentKey,
            IframeUrl = $"{_settings.BaseUrl}/api/acceptance/iframes/{_settings.IframeId}?payment_token={paymentKey}",
            OrderId = orderId,
            Amount = invoice.TotalAmount
        };
    }

    // ==================== Save Card (Zero-Amount) ====================

    public async Task<SaveCardResponse> InitiateSaveCardAsync(int customerId)
    {
        var customer = await _db.Customers.FindAsync(customerId)
            ?? throw new KeyNotFoundException("Customer not found");

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var specialRef = $"card_save_{customerId}_{timestamp}";

            var (result, errorDetail) = await CreateIntentionWithErrorAsync(
                100, "EGP", specialRef, // 1 EGP — auto-refunded after card is saved
                customer.Firstname, customer.Lastname, customer.Email, customer.PhoneNumber,
                saveCard: true);

            if (result == null)
                return new SaveCardResponse { Success = false, ErrorMessage = $"Failed to create save-card intention: {errorDetail}" };

            var intentionData = result.Value;
            var clientSecret = intentionData.GetProperty("client_secret").GetString()!;
            var paymentKey = "";
            if (intentionData.TryGetProperty("payment_keys", out var keys) && keys.GetArrayLength() > 0)
                paymentKey = keys[0].GetProperty("key").GetString() ?? "";

            return new SaveCardResponse
            {
                Success = true,
                ClientSecret = clientSecret,
                PaymentKey = paymentKey,
                IframeUrl = $"{_settings.BaseUrl}/api/acceptance/iframes/{_settings.IframeId}?payment_token={paymentKey}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InitiateSaveCardAsync failed for customer {CustomerId}", customerId);
            return new SaveCardResponse { Success = false, ErrorMessage = $"Card save initiation failed: {ex.Message}" };
        }
    }

    // ==================== Webhook Handler ====================

    public async Task HandleWebhookAsync(JsonElement payload, string hmacFromPaymob)
    {
        _logger.LogInformation("Webhook received. HMAC length: {HmacLength}", hmacFromPaymob?.Length ?? 0);

        // Diagnostic: store the webhook payload
        lock (_lock)
        {
            _lastWebhooks.Add(new { ReceivedAt = DateTime.UtcNow, Hmac = hmacFromPaymob, Payload = payload.ToString() });
            while (_lastWebhooks.Count > 5) _lastWebhooks.RemoveAt(0);
        }

        var type = payload.TryGetProperty("type", out var t) ? t.GetString() : "";

        // Route by webhook type
        if (string.Equals(type, "TOKEN", StringComparison.OrdinalIgnoreCase))
        {
            await HandleTokenCallbackAsync(payload, hmacFromPaymob ?? "");
            return;
        }

        if (string.Equals(type, "REFUND", StringComparison.OrdinalIgnoreCase))
            return;

        // Transaction callback
        await HandleTransactionCallbackAsync(payload, hmacFromPaymob ?? "");
    }

    // ==================== Token Callback (Card Saved) ====================

    private async Task HandleTokenCallbackAsync(JsonElement payload, string hmacFromPaymob)
    {
        if (!payload.TryGetProperty("obj", out var obj))
            return;

        // HMAC verification for TOKEN type
        // Fields in lexicographic order: card_subtype, created_at, email, id, masked_pan, merchant_id, order_id, token
        var cardSubtype = obj.TryGetProperty("card_subtype", out var cs) ? cs.GetString() ?? "" : "";
        var createdAt = obj.TryGetProperty("created_at", out var ca) ? ca.GetString() ?? "" : "";
        var email = obj.TryGetProperty("email", out var em) ? em.GetString() ?? "" : "";
        var id = obj.TryGetProperty("id", out var idp) ? idp.GetRawText() : "";
        var maskedPan = obj.TryGetProperty("masked_pan", out var mp) ? mp.GetString() ?? "" : "";
        var merchantId = obj.TryGetProperty("merchant_id", out var mi) ? mi.GetRawText() : "";
        var orderId = obj.TryGetProperty("order_id", out var oi) ? oi.GetString() ?? "" : "";
        var token = obj.TryGetProperty("token", out var tk) ? tk.GetString() ?? "" : "";

        var concatenated = cardSubtype + createdAt + email + id + maskedPan + merchantId + orderId + token;
        var computedHmac = ComputeHmacSha512(concatenated, _settings.HmacSecret);

        if (!string.IsNullOrEmpty(hmacFromPaymob) && !string.Equals(computedHmac, hmacFromPaymob, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("TOKEN HMAC Mismatch! Computed: {Computed}, Received: {Received}", computedHmac, hmacFromPaymob);
            return; // Silent fail for token callbacks
        }

        _logger.LogInformation("Token callback received: token={Token}, maskedPan={Pan}, subtype={Subtype}, orderId={OrderId}",
            token, maskedPan, cardSubtype, orderId);

        if (string.IsNullOrEmpty(token)) return;

        var lastFour = maskedPan.Length >= 4 ? maskedPan[^4..] : "0000";

        // ── Strategy 1: orderId IS the special_reference (e.g. "card_save_5_17180...")
        if (orderId.StartsWith("card_save_"))
        {
            var parts = orderId.Split('_');
            if (parts.Length >= 3 && int.TryParse(parts[2], out var directCustomerId))
            {
                await UpsertSavedCardAsync(directCustomerId, lastFour, cardSubtype, token);
                return;
            }

            _logger.LogWarning("TOKEN callback: could not parse customerId from card_save reference. orderId={OrderId}", orderId);
            return;
        }

        // ── Strategy 2: orderId is a Paymob internal ID (e.g. "78432165")
        //    Find the customer via email, then check if they have a pending card_save
        //    (a PaymentMethod with null token created by the TRANSACTION callback).
        //    Regular payments do NOT create PaymentMethod records, so this is safe.
        if (!string.IsNullOrEmpty(email))
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == email);
            if (customer != null)
            {
                var pendingCard = await _db.PaymentMethods.FirstOrDefaultAsync(pm =>
                    pm.CustomerId == customer.CustomerId
                    && pm.CardLastFour == lastFour
                    && pm.PaymobCardToken == null
                    && pm.IsActive && !pm.IsDeleted);

                if (pendingCard != null)
                {
                    _logger.LogInformation(
                        "TOKEN callback: found pending card_save for customer {CustomerId} via email lookup. Updating token.",
                        customer.CustomerId);

                    pendingCard.PaymobCardToken = token;
                    if (!string.IsNullOrEmpty(cardSubtype) && cardSubtype != "card")
                        pendingCard.CardBrand = cardSubtype;
                    pendingCard.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                    return;
                }
            }
        }

        // ── No match: regular payment TOKEN callback — ignore safely
        _logger.LogInformation("TOKEN callback: no card_save flow matched (orderId={OrderId}). Skipping.", orderId);
    }

    /// <summary>
    /// Creates or updates a saved card for a customer. Used by both TOKEN and TRANSACTION callbacks.
    /// </summary>
    private async Task UpsertSavedCardAsync(int customerId, string lastFour, string cardBrand, string? cardToken,
        string? holderName = null)
    {
        var customerExists = await _db.Customers.AnyAsync(c => c.CustomerId == customerId);
        if (!customerExists)
        {
            _logger.LogWarning("UpsertSavedCard: customer {CustomerId} not found.", customerId);
            return;
        }

        // Check for an existing card with the same last4
        var existingCard = await _db.PaymentMethods.FirstOrDefaultAsync(pm =>
            pm.CustomerId == customerId && pm.CardLastFour == lastFour
            && pm.IsActive && !pm.IsDeleted);

        if (existingCard != null)
        {
            // Update token only if we actually have one (TOKEN callback)
            if (!string.IsNullOrEmpty(cardToken))
                existingCard.PaymobCardToken = cardToken;
            if (!string.IsNullOrEmpty(cardBrand) && cardBrand != "card")
                existingCard.CardBrand = cardBrand;
            if (!string.IsNullOrEmpty(holderName))
                existingCard.CardHolderName = holderName;
            existingCard.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Updated PaymentMethod {Id} for customer {CustomerId} (token={HasToken})",
                existingCard.PaymentMethodId, customerId, !string.IsNullOrEmpty(cardToken));
            return;
        }

        // Create new card
        var nowUtc = DateTime.UtcNow;
        var hasCards = await _db.PaymentMethods.AnyAsync(pm =>
            pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted);

        // Format a display name if not provided (e.g. "Visa •••• 4242")
        var finalHolderName = holderName;
        if (string.IsNullOrEmpty(finalHolderName) || finalHolderName == "Saved Card")
        {
            var displayBrand = string.IsNullOrEmpty(cardBrand) || cardBrand.ToLower() == "card" 
                ? "Card" 
                : char.ToUpper(cardBrand[0]) + cardBrand[1..].ToLower();
            finalHolderName = $"{displayBrand} •••• {lastFour}";
        }

        var paymentMethod = new PaymentMethod
        {
            CustomerId = customerId,
            CardLastFour = lastFour,
            CardBrand = cardBrand,
            CardHolderName = finalHolderName,
            PaymentFunding = cardBrand.ToLower() switch
            {
                "debit" => PaymentFunding.Debit,
                "prepaid" => PaymentFunding.Prepaid,
                _ => PaymentFunding.Credit
            },
            PaymobCardToken = cardToken,
            IsDefault = !hasCards,
            IsActive = true,
            IsDeleted = false,
            AddedAt = nowUtc,
            CreatedAt = nowUtc
        };
        _db.PaymentMethods.Add(paymentMethod);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Saved new PaymentMethod for customer {CustomerId}: last4={Last4}, brand={Brand}, token={HasToken}",
            customerId, lastFour, cardBrand, !string.IsNullOrEmpty(cardToken));

        // Notify the customer
        _db.Notifications.Add(new Notification
        {
            UserId = customerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.OrderUpdated,
            Title = "Card saved successfully",
            Message = $"Your card ending in {lastFour} has been successfully added to your profile.",
            NotificationChannel = NotificationChannel.InApp,
            OrderId = 0
        });
        await _db.SaveChangesAsync();

        await _pusher.PushToCustomerAsync(
            customerId, "Card saved successfully",
            $"Your card ending in {lastFour} has been successfully added to your profile.",
            "CardSaved", 0);
    }

    // ==================== Transaction Callback ====================

    private async Task HandleTransactionCallbackAsync(JsonElement payload, string hmacFromPaymob)
    {
        if (!payload.TryGetProperty("obj", out var obj))
            return;

        if (obj.TryGetProperty("is_refunded", out var isRef) && isRef.ValueKind == JsonValueKind.True)
            return;

        // HMAC verification (Transaction type — alphabetical field order)
        var amount_cents = obj.GetProperty("amount_cents").GetRawText();
        var created_at = obj.GetProperty("created_at").GetString();
        var currency = obj.GetProperty("currency").GetString();
        var error_occured = obj.GetProperty("error_occured").GetBoolean().ToString().ToLower();
        var has_parent_transaction = obj.GetProperty("has_parent_transaction").GetBoolean().ToString().ToLower();
        var txId = obj.GetProperty("id").GetRawText();
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

        var concatenated = amount_cents + created_at + currency + error_occured + has_parent_transaction +
                        txId + integration_id + is_3d_secure + is_auth + is_capture + is_refunded +
                        is_standalone_payment + is_voided + order_id + owner + pending +
                        source_data_pan + source_data_sub_type + source_data_type + success;

        var computedHmac = ComputeHmacSha512(concatenated, _settings.HmacSecret);

        if (!string.Equals(computedHmac, hmacFromPaymob, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("HMAC Mismatch! Computed: {Computed}, Received: {Received}", computedHmac, hmacFromPaymob);
            throw new UnauthorizedAccessException("Invalid HMAC signature");
        }

        // Extract special_reference (maps to our orderId or card_save reference)
        var merchantOrderIdStr = obj.GetProperty("order").GetProperty("merchant_order_id").GetString() ?? "";

        // Also check special_reference (Intention API uses this)
        if (string.IsNullOrEmpty(merchantOrderIdStr) && obj.TryGetProperty("order", out var orderObj)
            && orderObj.TryGetProperty("special_reference", out var specRef))
        {
            merchantOrderIdStr = specRef.GetString() ?? "";
        }

        var transactionId = txId;
        var paymobOrderId = order_id;

        // Try to get token from source_data (legacy flow)
        string? cardToken = null;
        if (obj.GetProperty("source_data").TryGetProperty("token", out var tokenProp))
            cardToken = tokenProp.GetString();

        // Handle Card Save transaction
        if (merchantOrderIdStr.StartsWith("card_save_"))
        {
            _logger.LogInformation("Card save transaction callback: merchant_order_id={Id}, success={Success}",
                merchantOrderIdStr, success_bool);

            if (!success_bool)
            {
                var parts = merchantOrderIdStr.Split('_');
                if (parts.Length >= 3 && int.TryParse(parts[2], out var custId))
                    _logger.LogWarning("Card save payment failed for customer {CustomerId}", custId);
                return;
            }

            // Save or update the card via shared upsert logic.
            // Token may be null here (common with Intention API) — the TOKEN callback will fill it in.
            var saveParts = merchantOrderIdStr.Split('_');
            if (saveParts.Length >= 3 && int.TryParse(saveParts[2], out var customerId))
            {
                var lastFour = source_data_pan.Length >= 4 ? source_data_pan[^4..] : source_data_pan;
                var cardBrand = source_data_sub_type;

                var holderName = ResolveHolderName(obj);
                if (holderName == "Saved Card")
                {
                    // Instead of using the customer's database name (which might be a nickname),
                    // generate a clean name based on the card itself (e.g., "Visa •••• 4242")
                    var displayBrand = string.IsNullOrEmpty(cardBrand) || cardBrand.ToLower() == "card" 
                        ? "Card" 
                        : char.ToUpper(cardBrand[0]) + cardBrand[1..].ToLower();
                    
                    holderName = $"{displayBrand} •••• {lastFour}";
                }

                await UpsertSavedCardAsync(customerId, lastFour, cardBrand, cardToken, holderName);
            }

            // Auto-refund the verification charge back to the customer
            if (int.TryParse(amount_cents, out var refundAmount) && refundAmount > 0)
            {
                _logger.LogInformation("Auto-refunding card-save verification charge: txId={TxId}, amount={Amount} cents", txId, refundAmount);
                _ = Task.Run(() => RefundTransactionAsync(txId, refundAmount));
            }

            return;
        }

        // Regular order payment
        // special_reference format: "{orderId}" (legacy) or "{orderId}_{timestamp}" (new)
        var orderIdPart = merchantOrderIdStr.Contains('_') ? merchantOrderIdStr.Split('_')[0] : merchantOrderIdStr;
        if (!int.TryParse(orderIdPart, out var orderId))
            return;

        var order = await _db.Orders.Include(o => o.Invoices).FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order == null) return;

        var invoice = order.Invoices.FirstOrDefault(i => i.InvoiceStatus == InvoiceStatus.Pending || i.InvoiceStatus == InvoiceStatus.Draft);
        if (invoice == null) return;

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.OrderIdFromGateway == paymobOrderId && p.InvoiceId == invoice.InvoiceId);
        var now = DateTime.UtcNow;

        if (success_bool)
        {
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

                // Only refresh metadata on an existing saved card (paid with a saved card).
                // Do NOT save new card tokens from regular one-time payments.
                if (payment.PaymentMethodId.HasValue)
                {
                    var paymentMethod = await _db.PaymentMethods
                        .FirstOrDefaultAsync(pm => pm.PaymentMethodId == payment.PaymentMethodId.Value);
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
        }
        else
        {
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

        // Customer notification
        await SendPaymentNotificationAsync(order.CustomerId, orderId, invoice.TotalAmount, success_bool);

        // Post-payment processing (assign employees, generate passes)
        if (success_bool)
            await ProcessPostPaymentAsync(orderId);
    }

    // ==================== Post-Payment Processing ====================

    private async Task ProcessPostPaymentAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Package)
            .Include(o => o.Flight)
            .FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order?.Package == null) return;

        var packageName = order.Package.PackageName;

        if (packageName == PackageNames.DoorToDoor)
            await _doorToDoorOrderService.AssignEmployeesAfterPaymentAsync(orderId);
        else if (packageName == PackageNames.CarServiceToAirport || packageName == PackageNames.CarServiceFromAirport)
            await _carServiceOrderService.AssignEmployeesAfterPaymentAsync(orderId);
        else if (packageName == PackageNames.TrackingBaggage)
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

        // Trigger flight delay prediction immediately in background on successful payment
        // We create a new DI scope because the current scoped services (DbContext, etc.) will be
        // disposed when this HTTP request ends, before the background task completes.
        // We capture only the FlightId (a plain int) to avoid passing a tracked entity across scopes.
        if (order.FlightId > 0)
        {
            var capturedFlightId = order.FlightId;
            var capturedOrderId = orderId;
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                try
                {
                    var scopedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var flight = await scopedDb.Flights.FindAsync(capturedFlightId);
                    if (flight == null)
                    {
                        _logger.LogWarning("Flight {FlightId} not found when triggering delay prediction for order {OrderId}", capturedFlightId, capturedOrderId);
                        return;
                    }

                    var predictionService = scope.ServiceProvider.GetRequiredService<IFlightPredictionService>();
                    await predictionService.PredictAndNotifyFlightDelayAsync(flight);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to trigger flight delay prediction for paid order {OrderId}", capturedOrderId);
                }
            });
        }

        // Generate boarding passes
        if (packageName is PackageNames.DoorToDoor or PackageNames.CarServiceToAirport)
        {
            _ = Task.Run(async () =>
            {
                try { await _customerOrderService.GenerateBoardingPassesAsync(orderId); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to auto-generate boarding passes for order {OrderId}", orderId); }
            });
        }
    }

    private async Task SendPaymentNotificationAsync(int customerId, int orderId, decimal amount, bool success)
    {
        var title = success ? "Payment successful" : "Payment failed";
        var message = success
            ? $"Your payment of {amount} EGP for order #{orderId} has been received"
            : $"Your payment for order #{orderId} was not successful. Please try again.";
        var eventType = success ? "PaymentSuccess" : "PaymentFailed";

        _db.Notifications.Add(new Notification
        {
            UserId = customerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.OrderUpdated,
            Title = title,
            Message = message,
            NotificationChannel = NotificationChannel.InApp,
            OrderId = orderId
        });
        await _db.SaveChangesAsync();
        await _pusher.PushToCustomerAsync(customerId, title, message, eventType, orderId);
    }

    // ==================== Payment Status ====================

    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Invoices)
            .FirstOrDefaultAsync(o => o.OrderId == orderId)
            ?? throw new KeyNotFoundException("Order not found");

        var invoice = order.Invoices.OrderByDescending(i => i.CreatedAt).FirstOrDefault();

        return new PaymentStatusResponse
        {
            OrderId = orderId,
            OrderStatus = order.OrderStatus.ToString(),
            InvoiceStatus = invoice?.InvoiceStatus.ToString() ?? "N/A",
            Amount = invoice?.TotalAmount ?? 0,
            PaidAt = invoice?.PaidAt
        };
    }

    // ==================== Helpers ====================

    private static string ComputeHmacSha512(string data, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        using var hmac = new HMACSHA512(keyBytes);
        var hashBytes = hmac.ComputeHash(dataBytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    /// <summary>
    /// Extracts cardholder name from Paymob webhook payload's shipping_data.
    /// Returns "Saved Card" if no usable name is found.
    /// </summary>
    private static string ResolveHolderName(JsonElement obj)
    {
        if (obj.TryGetProperty("order", out var orderData)
            && orderData.TryGetProperty("shipping_data", out var shipData))
        {
            var fn = shipData.TryGetProperty("first_name", out var fnp) ? fnp.GetString() ?? "" : "";
            var ln = shipData.TryGetProperty("last_name", out var lnp) ? lnp.GetString() ?? "" : "";
            if (!string.IsNullOrEmpty(fn) && fn != "NA")
                return $"{fn} {ln}".Trim();
        }

        return "Saved Card";
    }


}
