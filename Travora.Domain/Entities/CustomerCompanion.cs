using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class CustomerCompanion : IHasTimestamps
{
    public int RelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int CustomerId { get; set; }
    public int CompanionId { get; set; }

    // Navigation properties
    public Customer Customer { get; set; } = null!;
    public Companion Companion { get; set; } = null!;
}
