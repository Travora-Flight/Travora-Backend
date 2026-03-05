namespace Travora.Domain.Entities;

public class BaggagePhoto
{
    public int PhotoId { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public int ImageSizeKb { get; set; }
    public DateTime CaptureTimestamp { get; set; } = DateTime.UtcNow;

    // Foreign keys
    public int? CapturedByEmployeeId { get; set; }
    public int? CapturedByCustomerId { get; set; }
    public int BaggageId { get; set; }
    public int? CheckpointId { get; set; }

    // Navigation properties
    public Employee? CapturedByEmployee { get; set; }
    public Customer? CapturedByCustomer { get; set; }
    public Baggage Baggage { get; set; } = null!;
    public Checkpoint? Checkpoint { get; set; }
}
