using Microsoft.AspNetCore.Http;
using Travora.Application.DTOs.External.Airline;

namespace Travora.Application.DTOs.Orders.DoorToDoor;

public class ValidateFlightRequest
{
    // Note: PassportNumber is not needed, it will be taken from JWT.
    // However, the spec says: "passportNumber مش محتاج — بيتاخد من الـ JWT تلقائياً"
    // I will let it be read from JWT inside the controller.
    public string TicketNumber { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string FlightDate { get; set; } = string.Empty;
    public int BaggageCount { get; set; }
}

public class ValidateFlightResponse
{
    public bool IsValid { get; set; }
    public AirlineFlightInfo? FlightInfo { get; set; }
    public AirlinePassengerInfo? PassengerInfo { get; set; }
    public int BaggageCount { get; set; }
    public DateTime? BookingDeadlineUtc { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ValidateCompanionRequest
{
    public string PassportNumber { get; set; } = string.Empty;
    public string TicketNumber { get; set; } = string.Empty;
    public IFormFile? PassportImage { get; set; }
}

public class ValidateCompanionResponse
{
    public bool IsValid { get; set; }
    public CompanionDetails? Companion { get; set; }
    public int TotalCompanions { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CompanionDetails
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string SeatNumber { get; set; } = string.Empty;
    public string TravelClass { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
    public string PassportImageUrl { get; set; } = string.Empty;
    public string? Nationality { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime? PassportExpiryDate { get; set; }
}
