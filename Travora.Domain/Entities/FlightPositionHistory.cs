using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class FlightPositionHistory : IHasTimestamps
{
    public int PositionId { get; set; }
    public decimal Altitude { get; set; }
    public decimal Direction { get; set; }
    public decimal HorizontalSpeed { get; set; }
    public decimal VerticalSpeed { get; set; }
    public bool IsOnGround { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime PositionTimestamp { get; set; }
    public int SequenceOrder { get; set; }
    public string? Squawk { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int FlightId { get; set; }

    // Navigation properties
    public Flight Flight { get; set; } = null!;
}
