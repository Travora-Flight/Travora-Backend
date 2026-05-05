namespace Travora.Application.DTOs.Flights.Tracker;

public class TimetableFlightDto
{
    public string FlightIata { get; set; } = string.Empty;
    public string AirlineName { get; set; } = string.Empty;
    public string AirlineIata { get; set; } = string.Empty;
    public string? AirlineLogoUrl { get; set; }

    // Departure info
    public string? DepartureIata { get; set; }
    public string? DepartureCity { get; set; }
    public string? DepartureScheduledTime { get; set; }
    public string? DepartureEstimatedTime { get; set; }
    public string? DepartureActualTime { get; set; }
    public string? DepartureGate { get; set; }
    public string? DepartureTerminal { get; set; }
    public int? DepartureDelay { get; set; }

    // Arrival info
    public string? ArrivalIata { get; set; }
    public string? ArrivalCity { get; set; }
    public string? ArrivalScheduledTime { get; set; }
    public string? ArrivalEstimatedTime { get; set; }
    public string? ArrivalGate { get; set; }
    public string? ArrivalTerminal { get; set; }

    public string Status { get; set; } = string.Empty;
}

public class TimetableResponse
{
    public string AirportIata { get; set; } = string.Empty;
    public string AirportName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "departure" or "arrival"
    public int Count { get; set; }
    public List<TimetableFlightDto> Flights { get; set; } = new();
}
