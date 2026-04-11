namespace Travora.Application.DTOs.Flights.Tracker;

public class FlightSearchResponse
{
    public List<AirportSearchItem> Airports { get; set; } = new();
    public List<FlightSearchItem> Flights { get; set; } = new();
}
