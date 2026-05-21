using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class BaggageTracking
{
    public int TrackingId { get; set; }
    public BaggageTrackingStatus Status { get; set; }
    public DateTime ArrivalTime { get; set; }
    public decimal GpsLatitude { get; set; }
    public decimal GpsLongitude { get; set; }

    // Foreign keys
    public int? HandledByEmployeeId { get; set; }
    public int BaggageId { get; set; }
    public int? CheckpointId { get; set; }
    public int? TriggeredByScanId { get; set; }

    // Navigation properties
    public Employee? HandledByEmployee { get; set; }
    public Baggage Baggage { get; set; } = null!;
    public Checkpoint? Checkpoint { get; set; }
    public QrScan? TriggeredByScan { get; set; }
}
