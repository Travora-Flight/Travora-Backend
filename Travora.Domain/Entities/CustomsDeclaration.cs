using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class CustomsDeclaration : IHasTimestamps
{
    public int CustomsId { get; set; }
    public CustomsType CustomsType { get; set; }
    public decimal TotalDeclaredValue { get; set; } = 0;
    public decimal TotalCustomsFee { get; set; } = 0;
    public DateTime DeclarationTimestamp { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int OrderId { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public ICollection<CustomsItem> CustomsItems { get; set; } = new List<CustomsItem>();
}
