namespace Travora.Application.DTOs.Flights.Tracker;

public class FlightSearchItem
{
    public string FlightIata { get; set; } = string.Empty;
    public string AirlineIata { get; set; } = string.Empty;
    public string Registration { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Altitude { get; set; }
    public string DepartureIata { get; set; } = string.Empty;
    public string ArrivalIata { get; set; } = string.Empty;
    public string? AirlineLogoUrl { get; set; }
    public string? AircraftImageUrl { get; set; }
    public string? AircraftModel { get; set; }
    public string? AircraftCountry { get; set; }
    
    // Departure Airport Details
    public string? DepartureAirportName { get; set; }
    public string? DepartureCity { get; set; }
    public string? DepartureUtc { get; set; }

    // Arrival Airport Details
    public string? ArrivalAirportName { get; set; }
    public string? ArrivalCity { get; set; }
    public string? ArrivalUtc { get; set; }

    public string Delay { get; set; } = "No delay";
}
