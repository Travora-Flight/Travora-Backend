using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class CustomsItem : IHasTimestamps
{
    public int CustomsItemId { get; set; }
    public ItemType ItemType { get; set; }
    public string ItemDescription { get; set; } = string.Empty;
    public decimal DeclaredValue { get; set; }
    public int Quantity { get; set; }
    public decimal TotalValue { get; set; }
    public decimal CustomsRatePercentage { get; set; }
    public decimal TotalCustomsValue { get; set; }
    public string ExternalCategoryId { get; set; } = string.Empty;
    public string ExternalCategoryName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int CustomsId { get; set; }

    // Navigation properties
    public CustomsDeclaration CustomsDeclaration { get; set; } = null!;
    public ICollection<CustomsItemInvoice> Invoices { get; set; } = new List<CustomsItemInvoice>();
}
