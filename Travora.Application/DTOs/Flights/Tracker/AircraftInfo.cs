namespace Travora.Application.DTOs.Flights.Tracker;

public class AircraftInfo
{
    public string Registration { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? Type { get; set; }
    public int? Age { get; set; }
    public string? Engines { get; set; }
    public string? ImageUrl { get; set; }
}
