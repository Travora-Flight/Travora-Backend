using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class PaymentMethod : IHasTimestamps, ISoftDelete
{
    public int PaymentMethodId { get; set; }
    public PaymentFunding PaymentFunding { get; set; }
    public string CardLastFour { get; set; } = string.Empty;
    public string CardHolderName { get; set; } = string.Empty;
    public int CardExpiryMonth { get; set; }
    public int CardExpiryYear { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public string? PaymobCardToken { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int CustomerId { get; set; }

    // Navigation properties
    public Customer Customer { get; set; } = null!;
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
