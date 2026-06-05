namespace Travora.Application.DTOs.Flights.Tracker;

/// <summary>
/// Response wrapper for live flights within a map viewport.
/// </summary>
public class ViewportFlightsResponse
{
    /// <summary>Total number of flights in the viewport.</summary>
    public int Count { get; set; }

    /// <summary>Server UTC timestamp when this response was generated.</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Unix timestamp (seconds) of when the Aviation Edge API data was last fetched.
    /// Frontend should use this to calculate elapsed time for LERP interpolation.
    /// </summary>
    public long LastApiUpdate { get; set; }

    public List<ViewportFlightDto> Flights { get; set; } = new();
}
