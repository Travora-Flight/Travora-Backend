namespace Travora.Infrastructure.Services;

/// <summary>
/// Internal model for Smart Merge Cache — wraps flight data with lastSeen timestamp.
/// </summary>
internal class CachedFlight
{
    public string FlightIata { get; set; } = string.Empty;
    public decimal Lat { get; set; }
    public decimal Lng { get; set; }
    public decimal Alt { get; set; }
    public decimal Hdg { get; set; }
    public decimal Spd { get; set; }
    public bool Gnd { get; set; }
    public string Sts { get; set; } = string.Empty;
    public string Airline { get; set; } = string.Empty;
    public string Dep { get; set; } = string.Empty;
    public string Arr { get; set; } = string.Empty;
    public string Reg { get; set; } = string.Empty;
    public string AircraftType { get; set; } = string.Empty;
    public long LastSeen { get; set; }
}
