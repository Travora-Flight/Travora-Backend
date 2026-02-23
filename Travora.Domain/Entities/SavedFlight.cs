namespace Travora.Domain.Entities;

public class SavedFlight
{
    public int SavedFlightId { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public bool NotificationEnabled { get; set; } = true;

    // Foreign keys
    public int CustomerId { get; set; }
    public int FlightId { get; set; }

    // Navigation properties
    public Customer Customer { get; set; } = null!;
    public Flight Flight { get; set; } = null!;
}
