using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Payment : IHasTimestamps
{
    public int PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";
    public string? OrderIdFromGateway { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string TransactionId { get; set; } = string.Empty;
    public string PaymentGateway { get; set; } = string.Empty;
    public string? GatewayResponse { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int InvoiceId { get; set; }

    /// <summary>
    /// Nullable: only set when the customer pays with a previously saved card.
    /// One-time card payments do not create or reference a PaymentMethod.
    /// </summary>
    public int? PaymentMethodId { get; set; }

    // Navigation properties
    public Invoice Invoice { get; set; } = null!;
    public PaymentMethod? PaymentMethod { get; set; }
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}
