using Travora.Application.DTOs.External.Airline;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Domain.Enums;

namespace Travora.Application.DTOs.Orders.CarService;

// ===== Step 1 — Validate Flight =====
public class CarServiceValidateFlightRequest
{
    public string TicketNumber { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string FlightDate { get; set; } = string.Empty;
    public int BaggageCount { get; set; }
    public CarServiceType ServiceType { get; set; }
}

public class CarServiceValidateFlightResponse
{
    public bool IsValid { get; set; }
    public AirlineFlightInfo? FlightInfo { get; set; }
    public AirlinePassengerInfo? PassengerInfo { get; set; }
    public int BaggageCount { get; set; }
    public DateTime? BookingDeadlineUtc { get; set; }
    public CarServiceType ServiceType { get; set; }
    public string? ErrorMessage { get; set; }
}

// ===== Step 3 — Resolve Location (no LocationType needed) =====
public class CarServiceResolveLocationRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class AvailableDatesResponse
{
    public bool IsValid { get; set; }
    public List<DateTime> AvailableDates { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

// ===== Step 5 — My Bags (delivery_from_airport only) =====
public class MyBagsResponse
{
    public bool IsValid { get; set; }
    public List<PassengerBagItem> Passengers { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class PassengerBagItem
{
    public string PassengerName { get; set; } = string.Empty;
    public string TicketNumber { get; set; } = string.Empty;
    public List<BagItem> Bags { get; set; } = new();
}

public class BagItem
{
    public string TagNumber { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public string Journey { get; set; } = string.Empty;
    public string Gate { get; set; } = string.Empty;
    public string Terminal { get; set; } = string.Empty;
    public string? TicketNumber { get; set; }
}

public class SelectBagsRequest
{
    public List<string> SelectedTagNumbers { get; set; } = new();
}

public class CarServiceUpdateLocationRequest
{
    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
}
