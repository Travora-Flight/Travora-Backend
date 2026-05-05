using Travora.Application.DTOs.Flights.Tracker;

namespace Travora.Application.Interfaces.Services;

public interface IFlightTrackerService
{
    /// <summary>
    /// Get live flights within the viewport bounds.
    /// Uses global cache for zoom-out, Aviation Edge with lat/lng/distance for zoom-in.
    /// </summary>
    Task<ViewportFlightsResponse> GetViewportFlightsAsync(
        decimal minLat, decimal maxLat, decimal minLng, decimal maxLng,
        bool isZoomedIn = false, decimal? centerLat = null, decimal? centerLng = null, int? distance = null);

    /// <summary>
    /// Get full flight details including trail, schedule, and aircraft info.
    /// Trail comes from /flight_track_history endpoint.
    /// </summary>
    Task<FlightDetailsResponse?> GetFlightDetailsAsync(string flightIata);

    /// <summary>
    /// Hybrid search: DB first for cities/airports/airlines, Redis cache for flights, API fallback.
    /// </summary>
    Task<FlightSearchResponse> SearchAsync(string q);

    /// <summary>
    /// Get airports within viewport bounds from local DB.
    /// </summary>
    Task<AirportViewportResponse> GetAirportsInViewportAsync(
        decimal minLat, decimal maxLat, decimal minLng, decimal maxLng);

    /// <summary>
    /// Get airport departure/arrival timetable.
    /// </summary>
    Task<TimetableResponse> GetAirportTimetableAsync(string airportCode, string type = "departure");
}
