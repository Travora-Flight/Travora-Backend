namespace Travora.Application.DTOs.Flights.Tracker;

public class LiveFlightsResponse
{
    public int Count { get; set; }
    public List<LiveFlightDto> Flights { get; set; } = new();
}
