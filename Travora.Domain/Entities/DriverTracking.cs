using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class DriverTracking : IHasTimestamps
{
    public int TrackingId { get; set; }

    // Location Data
    public decimal GpsLatitude { get; set; }
    public decimal GpsLongitude { get; set; }
    public decimal? AccuracyMeters { get; set; }
    public decimal? SpeedKmh { get; set; }
    public decimal? HeadingDegrees { get; set; }

    // Tracking Info
    public DateTime TrackedAt { get; set; } = DateTime.UtcNow;
    public bool IsMoving { get; set; } = true;
    public bool IsOnline { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int DriverId { get; set; }
    public int? OrderServiceId { get; set; }

    // Navigation properties
    public Employee Driver { get; set; } = null!;
    public OrderService? OrderService { get; set; }
}
