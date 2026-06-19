namespace Travora.Application.DTOs.External.Airline;

public class AirlineValidateTicketRequest
{
    public string PassportNumber { get; set; } = string.Empty;
    public string TicketNumber { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string FlightDate { get; set; } = string.Empty;
}

public class AirlineValidateTicketResponse
{
    public bool IsValid { get; set; }
    public List<string>? Errors { get; set; }

    // Root level data
    public AirlineFlightInfo? Flight { get; set; }
    public AirlinePassengerInfo? Passenger { get; set; }
    public string? FlightDate { get; set; }
    public string? FlightDuration { get; set; }
    public DateTime? BoardingTimeUtc { get; set; }
    public string? Terminal { get; set; }
    public string? Gate { get; set; }

    // Ticket level data
    public AirlineTicketData? Ticket { get; set; }

    // Legacy support
    public AirlineFlightInfo? FlightInfo { get; set; }
    public AirlinePassengerInfo? PassengerInfo { get; set; }
}

public class AirlineTicketData
{
    public string? SeatNumber { get; set; }
    public string? TravelClass { get; set; }
    public string? BoardingStatus { get; set; }
    public AirlineFlightInfo? Flight { get; set; }
    public AirlinePassengerInfo? Passenger { get; set; }
}

public class AirlineFlightInfo
{
    public string FlightNumber { get; set; } = string.Empty;
    public string DepartureAirport { get; set; } = string.Empty;
    public string ArrivalAirport { get; set; } = string.Empty;
    public DateTime DepartureTimeUtc { get; set; }
    public DateTime? ArrivalTimeUtc { get; set; }
    public string? Terminal { get; set; }
    public string? Gate { get; set; }
    public string? AirlineName { get; set; }
    public string? AirlineIcaoCode { get; set; }
    public string? DepartureIataCode { get; set; }
    public string? ArrivalIataCode { get; set; }
    public string? OriginCity { get; set; }
    public string? DestinationCity { get; set; }
    public string? FlightDate { get; set; }
    public string? FlightDuration { get; set; }
    public DateTime? BoardingTimeUtc { get; set; }
}

public class AirlinePassengerInfo
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PassportNumber { get; set; }
    public string? Nationality { get; set; }
    public string? DateOfBirth { get; set; }
    public string? PassportExpiryDate { get; set; }
    public string? SeatNumber { get; set; }
    public string? TravelClass { get; set; }
    public string? BoardingStatus { get; set; }
}

public class AirlineBaggageCheckResponse
{
    public string? PassengerName { get; set; }
    public string? PassportNumber { get; set; }
    public int TotalBaggageCount { get; set; }
    public List<AirlineBaggageTicket>? Tickets { get; set; }
    
    // Legacy support
    public string TicketNumber { get; set; } = string.Empty;
    public int BaggageCount => TotalBaggageCount;
}

public class AirlineBaggageTicket
{
    public string? TicketNumber { get; set; }
    public string? FlightNumber { get; set; }
    public string? DepartureAirport { get; set; }
    public string? ArrivalAirport { get; set; }
    public int BaggageCount { get; set; }
    public List<AirlineBaggageTag>? BaggageTags { get; set; }
}

public class AirlineBaggageTag
{
    public int BaggageTagId { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public int TicketId { get; set; }
    public decimal WeightKg { get; set; }
    public string? Origin { get; set; }
    public string? Destination { get; set; }
    public string? Terminal { get; set; }
    public string? Gate { get; set; }
    public string? PassengerName { get; set; }
    public string? SeatNumber { get; set; }
    public string? CurrentLocation { get; set; }
}

public class AirlineCustomsLookupResponse
{
    public bool Found { get; set; }
    public AirlineCustomsProduct? Product { get; set; }

    // Support flat response from Airline API
    public string? ProductName { get; set; }
    public string? CategoryName { get; set; }
    public decimal? Rate { get; set; }
}

public class AirlineCustomsProduct
{
    public string Name { get; set; } = string.Empty;
    public decimal CustomsRate { get; set; }
    public string? Category { get; set; }
    public string? SubCategory { get; set; }

    // Support common API field names
    public string? ProductName { get; set; }
    public string? CategoryName { get; set; }
    public decimal? Rate { get; set; }
}

public class AirlineCustomsCategoryResponse
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SubCategoriesCount { get; set; }
}

// ===== Baggage By Ticket (GET /api/airline/baggage/by-ticket/{ticketNumber}) =====
public class AirlineBaggageByTicketResponse
{
    public string TicketNumber { get; set; } = string.Empty;
    public List<AirlineBagItem> Bags { get; set; } = new();
}

public class AirlineBagItem
{
    public string TagNumber { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public string CurrentLocation { get; set; } = string.Empty;
    public DateTime? LastLocationUpdatedAt { get; set; }
}

// ===== Issue Boarding Pass (POST /api/airline/issue-boarding-pass) =====
public class AirlineIssueBoardingPassRequest
{
    public string TicketNumber { get; set; } = string.Empty;
}

public class AirlineIssueBoardingPassWrapper
{
    public List<AirlineIssueBoardingPassResponse>? BoardingPasses { get; set; }
}

public class AirlineIssueBoardingPassResponse
{
    public string? PassengerName { get; set; }
    public string? SeatNumber { get; set; }
    public string? Gate { get; set; }
    public string? Terminal { get; set; }
    public string? Class { get; set; }
    public string? BoardingTime { get; set; }
    public string? FlightDate { get; set; }
    public string? BarcodeData { get; set; }
    public string? FlightNumber { get; set; }
    public string? AirlineName { get; set; }
    public string? AirlineIataCode { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? FromCity { get; set; }
    public string? ToCity { get; set; }
    public string? DepartureTime { get; set; }
    public string? ArrivalTime { get; set; }
    public string? Duration { get; set; }
}

public class AirlineBaggageAllowanceResponse
{
    public string TicketNumber { get; set; } = string.Empty;
    public int AllowedBaggageCount { get; set; }
    public decimal MaxAllowedWeight { get; set; }
}
