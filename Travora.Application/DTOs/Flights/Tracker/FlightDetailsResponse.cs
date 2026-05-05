namespace Travora.Application.DTOs.Flights.Tracker;

public class FlightDetailsResponse
{
    public string FlightIata { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public FlightDetailAirlineDto Airline { get; set; } = new();
    public AircraftInfo Aircraft { get; set; } = new();
    public FlightDetailAirportDto Departure { get; set; } = new();
    public FlightDetailAirportDto Arrival { get; set; } = new();

    public FlightPosition Position { get; set; } = new();


    public string? DelayMessage { get; set; }

    public List<FlightTrailPoint> Trail { get; set; } = new();
}
