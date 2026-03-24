using Travora.Application.DTOs.External.Airline;
using Travora.Application.DTOs.Orders.DoorToDoor;

namespace Travora.Application.DTOs.Orders.BagTracking;

public class BagTrackingDraftOrder
{
    public string CustomerId { get; set; } = string.Empty;
    public string TicketNumber { get; set; } = string.Empty;
    public AirlineFlightInfo? FlightInfo { get; set; }
    public AirlinePassengerInfo? PassengerInfo { get; set; }
    public int BaggageCount { get; set; }
    public DateTime BookingDeadlineUtc { get; set; }
    public List<DraftCompanion> Companions { get; set; } = new();

    // Step 2.5 Validation
    public int TotalBaggageCount { get; set; }
    public bool BaggageValidated { get; set; }

    // Step 3 Scanned Bags
    public List<DraftScannedBag> ScannedBags { get; set; } = new();
}

public class DraftScannedBag
{
    public string TagNumber { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public string? Destination { get; set; }
    public DateTime ScannedAt { get; set; }
    public List<string> Photos { get; set; } = new();
}
