namespace Travora.Application.DTOs.Flights.Tracker;

public class FlightDetailAirportDto
{
    public string Iata { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Utc { get; set; } = string.Empty;
    public string ScheduledTime { get; set; } = string.Empty;
}
