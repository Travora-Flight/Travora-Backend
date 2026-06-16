namespace Travora.Application.DTOs.Airports;

public class AirportFlightDto
{
    public string Destination { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string ScheduledTime { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Gate { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Delay { get; set; }
}
