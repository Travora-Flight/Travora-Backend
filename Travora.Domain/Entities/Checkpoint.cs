using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Checkpoint
{
    public int CheckpointId { get; set; }
    public string CheckpointName { get; set; } = string.Empty;
    public CheckpointType CheckpointType { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SequenceOrder { get; set; }
    public decimal? GpsLatitude { get; set; }
    public decimal? GpsLongitude { get; set; }

    // Foreign keys
    public int? AirportId { get; set; }

    // Navigation properties
    public Airport? Airport { get; set; }
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public ICollection<QrScan> QrScans { get; set; } = new List<QrScan>();
    public ICollection<BaggagePhoto> BaggagePhotos { get; set; } = new List<BaggagePhoto>();
    public ICollection<BaggageTracking> BaggageTrackings { get; set; } = new List<BaggageTracking>();
}
