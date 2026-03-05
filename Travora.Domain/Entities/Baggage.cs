using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Baggage : IHasTimestamps
{
    public int BaggageId { get; set; }
    public BaggageOwnerType OwnerType { get; set; }
    public decimal? TotalWeight { get; set; }
    public string? BaggageNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int OrderId { get; set; }
    public int? CustomerId { get; set; }
    public int? CompanionId { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public Customer? Customer { get; set; }
    public Companion? Companion { get; set; }
    public ICollection<SecurityLock> SecurityLocks { get; set; } = new List<SecurityLock>();
    public ICollection<QrScan> QrScans { get; set; } = new List<QrScan>();
    public ICollection<BaggagePhoto> BaggagePhotos { get; set; } = new List<BaggagePhoto>();
    public ICollection<BaggageTracking> BaggageTrackings { get; set; } = new List<BaggageTracking>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
