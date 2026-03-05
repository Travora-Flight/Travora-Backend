namespace Travora.Domain.Entities;

public class QrScan
{
    public int ScanId { get; set; }
    public DateTime ScanTimestamp { get; set; } = DateTime.UtcNow;
    public decimal GpsLatitude { get; set; }
    public decimal GpsLongitude { get; set; }
    public string Description { get; set; } = string.Empty;

    // Foreign keys
    public int BaggageId { get; set; }
    public int? CheckpointId { get; set; }
    public int? ScannedByEmployeeId { get; set; }
    public int? ScannedByCustomerId { get; set; }

    // Navigation properties
    public Baggage Baggage { get; set; } = null!;
    public Checkpoint? Checkpoint { get; set; }
    public Employee? ScannedByEmployee { get; set; }
    public Customer? ScannedByCustomer { get; set; }
    public ICollection<BaggageTracking> TriggeredTrackings { get; set; } = new List<BaggageTracking>();
}
