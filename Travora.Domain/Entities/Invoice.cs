using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Invoice : IHasTimestamps
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal PackageFee { get; set; }
    public decimal CustomsFee { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public InvoiceStatus InvoiceStatus { get; set; } = InvoiceStatus.Draft;
    public string Currency { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public string InvoiceFilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int OrderId { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
