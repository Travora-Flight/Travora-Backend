namespace Travora.Application.DTOs.Flights.Tracker;

/// <summary>
/// Normalized aircraft position DTO parsed from ADSBexchange API response.
/// Field names match the existing ViewportFlightDto pattern for consistency.
/// Only includes fields relevant to Travora's tracking features.
/// </summary>
public class AdsbAircraftDto
{
    /// <summary>ICAO 24-bit hex code — unique aircraft identifier (e.g. "a4b605").</summary>
    public string Hex { get; set; } = string.Empty;

    /// <summary>Flight callsign (e.g. "MSR779"). May have trailing spaces — always trimmed.</summary>
    public string Callsign { get; set; } = string.Empty;

    /// <summary>Aircraft registration number (e.g. "SU-GDZ").</summary>
    public string Registration { get; set; } = string.Empty;

    /// <summary>ICAO aircraft type designator (e.g. "A320", "B738").</summary>
    public string AircraftType { get; set; } = string.Empty;

    /// <summary>Current latitude in decimal degrees.</summary>
    public decimal Lat { get; set; }

    /// <summary>Current longitude in decimal degrees.</summary>
    public decimal Lon { get; set; }

    /// <summary>Barometric altitude in feet. -1 if on ground.</summary>
    public decimal AltitudeFt { get; set; }

    /// <summary>Ground speed in knots.</summary>
    public decimal SpeedKts { get; set; }

    /// <summary>Track/heading in degrees (0-360) — used to rotate the airplane icon.</summary>
    public decimal Heading { get; set; }

    /// <summary>Whether the aircraft is on the ground.</summary>
    public bool IsOnGround { get; set; }

    /// <summary>Squawk transponder code (e.g. "1200", "7700" for emergency).</summary>
    public string Squawk { get; set; } = string.Empty;

    /// <summary>Emergency status: "none", "general", "lifeguard", "minfuel", etc.</summary>
    public string Emergency { get; set; } = string.Empty;

    /// <summary>Seconds since the last ADS-B message was received from this aircraft.</summary>
    public double SeenSeconds { get; set; }

    /// <summary>Aircraft category: "A1"=Light, "A2"=Small, "A3"=Large, "A5"=Heavy, "A7"=Rotorcraft.</summary>
    public string Category { get; set; } = string.Empty;
}
