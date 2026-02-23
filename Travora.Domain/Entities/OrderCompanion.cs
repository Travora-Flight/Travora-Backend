using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class OrderCompanion : IHasTimestamps
{
    public int OrderCompanionId { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int OrderId { get; set; }
    public int CompanionId { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public Companion Companion { get; set; } = null!;
}
