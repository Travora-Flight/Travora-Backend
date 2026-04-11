using Travora.Application.DTOs.Flights.Tracker;

namespace Travora.Application.Interfaces.Services;

public interface IFlightTrackerService
{
    Task<LiveFlightsResponse> GetLiveFlightsAsync(decimal? lat = null, decimal? lng = null, int? distance = null);
    Task<FlightSearchResponse> SearchAsync(string q);
    Task<FlightDetailsResponse?> GetFlightDetailsAsync(string flightIata);
}
