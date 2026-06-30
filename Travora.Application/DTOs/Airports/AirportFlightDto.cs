namespace Travora.Application.DTOs.Airports;

public class AirportFlightDto
{
    public string Destination { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string ScheduledTime { get; set; } = string.Empty;
    public string? EstimatedTime { get; set; }
    public string? ActualTime { get; set; }
    public string Time { get; set; } = string.Empty;
    public string Gate { get; set; } = string.Empty;
    public string? Terminal { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Delay { get; set; }
    public string AirlineName { get; set; } = string.Empty;
    public string AirlineIata { get; set; } = string.Empty;
    public string? AirlineLogoUrl { get; set; }
    public string City { get; set; } = string.Empty;
}
