namespace Travora.Application.DTOs.Payments;

public class PaymentInitiateRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PaymentInitiationResponse
{
    public bool Success { get; set; }
    public string PaymentKey { get; set; } = string.Empty;
    public string IframeUrl { get; set; } = string.Empty;
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
