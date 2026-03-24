using Travora.Application.DTOs.External.Airline;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Domain.Enums;

namespace Travora.Application.DTOs.Orders.CarService;

public class CarServiceDraftOrder
{
    public string CustomerId { get; set; } = string.Empty;
    public string TicketNumber { get; set; } = string.Empty;
    public AirlineFlightInfo? FlightInfo { get; set; }
    public AirlinePassengerInfo? PassengerInfo { get; set; }
    public int BaggageCount { get; set; }
    public DateTime BookingDeadlineUtc { get; set; }
    public CarServiceType ServiceType { get; set; }

    public List<DraftCompanion> Companions { get; set; } = new();

    // Step 2.5 Validation
    public int TotalBaggageCount { get; set; }
    public bool BaggageValidated { get; set; }

    // Step 4 Slots (single slot — pickup or delivery depending on ServiceType)
    public string? SelectedSlot { get; set; }
    public DateTime? SelectedSlotDate { get; set; }

    // Step 3 Location (single location — pickup for to_airport, delivery for from_airport)
    public double? LocationLatitude { get; set; }
    public double? LocationLongitude { get; set; }
    public string? LocationFormattedAddress { get; set; }
    public string? LocationStreetAddress { get; set; }
    public string? LocationCity { get; set; }
    public string? LocationState { get; set; }
    public string? LocationCountry { get; set; }
    public string? LocationPostalCode { get; set; }

    // Step 5 Selected Bags (delivery_from_airport only)
    public List<string> SelectedBagTags { get; set; } = new();
}
