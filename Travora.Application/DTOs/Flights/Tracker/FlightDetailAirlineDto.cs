namespace Travora.Application.DTOs.Flights.Tracker;

public class FlightDetailAirlineDto
{
    public string Name { get; set; } = string.Empty;
    public string Iata { get; set; } = string.Empty;
    public string? Logo { get; set; }
    public string? Callsign { get; set; }
}
