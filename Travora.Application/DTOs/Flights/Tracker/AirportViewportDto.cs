namespace Travora.Application.DTOs.Flights.Tracker;

/// <summary>
/// Airport marker on the map — sourced from local DB only.
/// </summary>
public class AirportViewportDto
{
    public string Iata { get; set; } = string.Empty;
    public string Icao { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public decimal Lat { get; set; }
    public decimal Lng { get; set; }
}

public class AirportViewportResponse
{
    public int Count { get; set; }
    public List<AirportViewportDto> Airports { get; set; } = new();
}
