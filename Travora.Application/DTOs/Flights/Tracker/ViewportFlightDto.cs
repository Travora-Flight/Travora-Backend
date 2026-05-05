namespace Travora.Application.DTOs.Flights.Tracker;

/// <summary>
/// Compact DTO for map markers — short keys to minimize payload (polled every 15 sec).
/// </summary>
public class ViewportFlightDto
{
    /// <summary>Flight IATA code, e.g. "MS801"</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Latitude</summary>
    public decimal Lat { get; set; }

    /// <summary>Longitude</summary>
    public decimal Lng { get; set; }

    /// <summary>Altitude in feet</summary>
    public decimal Alt { get; set; }

    /// <summary>Heading in degrees (0-360) — used to rotate the airplane icon</summary>
    public decimal Hdg { get; set; }

    /// <summary>Horizontal speed</summary>
    public decimal Spd { get; set; }

    /// <summary>Is on ground</summary>
    public bool Gnd { get; set; }

    /// <summary>Flight status: en-route, landed, started, etc.</summary>
    public string Sts { get; set; } = string.Empty;

    /// <summary>Airline IATA code</summary>
    public string Airline { get; set; } = string.Empty;

    /// <summary>Departure airport IATA</summary>
    public string Dep { get; set; } = string.Empty;

    /// <summary>Arrival airport IATA</summary>
    public string Arr { get; set; } = string.Empty;

    /// <summary>Aircraft registration number</summary>
    public string Reg { get; set; } = string.Empty;
}
