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
        decimal? centerLat = null, decimal? centerLng = null, int? distance = null);

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

    Task<Travora.Application.DTOs.Customer.Profile.SavedFlightsResponse> GetTrackedFlightsAsync(int? customerId, string? guestId);
    Task<(bool Success, string Message, int? SavedFlightId)> TrackFlightAsync(string flightIata, int? customerId, string? guestId);
    Task<(bool Success, string Message)> RemoveTrackedFlightAsync(int savedFlightId, int? customerId, string? guestId);
    Task<(bool Success, string Message, bool? NotificationEnabled)> ToggleTrackedFlightNotificationAsync(int savedFlightId, int? customerId, string? guestId);
    Task<(bool Success, string Message)> MergeGuestTrackedFlightsAsync(string guestId, int customerId);
}
