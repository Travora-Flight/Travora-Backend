using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Refund : IHasTimestamps
{
    public int RefundId { get; set; }
    public decimal RefundAmount { get; set; }
    public RefundStatus RefundStatus { get; set; } = RefundStatus.Requested;
    public string Reason { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string RefundTransactionId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int? ProcessedByAdminId { get; set; }
    public int OrderId { get; set; }
    public int PaymentId { get; set; }

    // Navigation properties
    public Admin? ProcessedByAdmin { get; set; }
    public Order Order { get; set; } = null!;
    public Payment Payment { get; set; } = null!;
}
