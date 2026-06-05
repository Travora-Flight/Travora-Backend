namespace Travora.Application.DTOs.Payments;

public class PaymentInitiateRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PaymentInitiationResponse
{
    public bool Success { get; set; }

    /// <summary>Client secret for Paymob Unified Checkout (Intention API).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Legacy payment key — kept for backward compatibility.</summary>
    public string PaymentKey { get; set; } = string.Empty;

    /// <summary>Iframe URL for legacy checkout flow (empty when using Unified Checkout).</summary>
    public string IframeUrl { get; set; } = string.Empty;

    /// <summary>3DS redirect URL when paying with a saved card. Frontend should redirect to this URL if present.</summary>
    public string? RedirectUrl { get; set; }

    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentStatusResponse
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string InvoiceStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime? PaidAt { get; set; }
}

public class SaveCardResponse
{
    public bool Success { get; set; }
    public string ClientSecret { get; set; } = string.Empty;
    public string PaymentKey { get; set; } = string.Empty;
    public string IframeUrl { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
