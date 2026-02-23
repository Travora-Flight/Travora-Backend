using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class BoardingPass
{
    public int BoardingPassId { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public string SeatNumber { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public TimeSpan BoardingTime { get; set; }
    public DateTime FlightDate { get; set; }
    public string Gate { get; set; } = string.Empty;
    public string Terminal { get; set; } = string.Empty;
    public string BarcodeData { get; set; } = string.Empty;
    public string QrCodePath { get; set; } = string.Empty;
    public BoardingStatus BoardingStatus { get; set; } = BoardingStatus.NotBoarded;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? BoardedAt { get; set; }

    // Foreign keys
    public int OrderId { get; set; }
    public int FlightId { get; set; }
    public int? CompanionId { get; set; }
    public int? CustomerId { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public Flight Flight { get; set; } = null!;
    public Companion? Companion { get; set; }
    public Customer? Customer { get; set; }
}
