namespace Travora.Application.DTOs.Flights.Tracker;

public class ViewportFlightsResponse
{
    public int Count { get; set; }
    public DateTime LastUpdated { get; set; }
    public List<ViewportFlightDto> Flights { get; set; } = new();
}
