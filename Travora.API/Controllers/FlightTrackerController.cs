using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Flights.Tracker;
using Travora.Application.Interfaces.Services;
using Travora.Application.DTOs.Customer.Profile;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/flights")]
public class FlightTrackerController : ControllerBase
{
    private readonly IFlightTrackerService _trackerService;
    private readonly IAirportDetailsService _airportDetailsService;

    public FlightTrackerController(IFlightTrackerService trackerService, IAirportDetailsService airportDetailsService)
    {
        _trackerService = trackerService;
        _airportDetailsService = airportDetailsService;
    }

    /// <summary>
    /// Get live flights within the viewport bounds.
    /// </summary>
    [HttpGet("live")]
    [ProducesResponseType(typeof(ViewportFlightsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetViewportFlights(
        [FromQuery] decimal minLat = -90,
        [FromQuery] decimal maxLat = 90,
        [FromQuery] decimal minLng = -180,
        [FromQuery] decimal maxLng = 180,
        [FromQuery] decimal? centerLat = null,
        [FromQuery] decimal? centerLng = null,
        [FromQuery] int? distance = null)
    {
        var result = await _trackerService.GetViewportFlightsAsync(minLat, maxLat, minLng, maxLng, centerLat, centerLng, distance);
        return Ok(result);
    }

    /// <summary>
    /// Get airports within viewport bounds.
    /// </summary>
    [HttpGet("airports")]
    [ProducesResponseType(typeof(AirportViewportResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAirportsInViewport(
        [FromQuery] decimal minLat = -90,
        [FromQuery] decimal maxLat = 90,
        [FromQuery] decimal minLng = -180,
        [FromQuery] decimal maxLng = 180)
    {
        var result = await _trackerService.GetAirportsInViewportAsync(minLat, maxLat, minLng, maxLng);
        return Ok(result);
    }



    /// <summary>
    /// Search for flights and airports by query string.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(FlightSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FlightTrackerGenericErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return BadRequest(new FlightTrackerGenericErrorResponse { Error = "Search query must be at least 2 characters" });

        var result = await _trackerService.SearchAsync(q.Trim());
        return Ok(result);
    }

    /// <summary>
    /// Get full flight details including trail, schedule, and aircraft info.
    /// </summary>
    [HttpGet("{flightIata}/details")]
    [ProducesResponseType(typeof(FlightDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FlightTrackerGenericErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFlightDetails(string flightIata)
    {
        var result = await _trackerService.GetFlightDetailsAsync(flightIata.Trim().ToUpper());

        if (result == null)
            return NotFound(new FlightTrackerGenericErrorResponse { Error = "Flight not found" });

        return Ok(result);
    }

    /// <summary>
    /// Get full airport details by IATA code.
    /// </summary>
    [HttpGet("/api/v1/airports/{code}/details")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAirportDetails(string code)
    {
        try
        {
            var result = await _airportDetailsService.GetAirportDetailsAsync(code.ToUpper());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    private int? GetCustomerId()
    {
        var claim = User.FindFirst("customerId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>
    /// List tracked flights for registered customer (via JWT) or guest (via guestId query parameter).
    /// </summary>
    [HttpGet("tracked")]
    [ProducesResponseType(typeof(SavedFlightsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrackedFlights([FromQuery] string? guestId = null)
    {
        var customerId = GetCustomerId();
        if (customerId == null && string.IsNullOrEmpty(guestId))
        {
            return BadRequest(new { error = "Either authorization token or guestId must be provided" });
        }

        var result = await _trackerService.GetTrackedFlightsAsync(customerId, guestId);
        return Ok(result);
    }

    /// <summary>
    /// Save/track a flight for registered customer (via JWT) or guest (via guestId).
    /// </summary>
    [HttpPost("track")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TrackFlight([FromBody] TrackFlightRequest request)
    {
        var customerId = GetCustomerId();
        if (customerId == null && string.IsNullOrEmpty(request.GuestId))
        {
            return BadRequest(new { error = "Either authorization token or guestId must be provided" });
        }

        var (success, message, savedFlightId) = await _trackerService.TrackFlightAsync(request.FlightIata, customerId, request.GuestId);
        if (!success)
        {
            return BadRequest(new { error = message });
        }

        return Ok(new { success, message, savedFlightId });
    }

    /// <summary>
    /// Stop tracking / remove a saved flight.
    /// </summary>
    [HttpDelete("tracked/{savedFlightId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteTrackedFlight(int savedFlightId, [FromQuery] string? guestId = null)
    {
        var customerId = GetCustomerId();
        if (customerId == null && string.IsNullOrEmpty(guestId))
        {
            return BadRequest(new { error = "Either authorization token or guestId must be provided" });
        }

        var (success, message) = await _trackerService.RemoveTrackedFlightAsync(savedFlightId, customerId, guestId);
        if (!success)
        {
            return StatusCode(403, new { error = message });
        }

        return Ok(new { success, message });
    }

    /// <summary>
    /// Toggle push notification alerts for a tracked flight.
    /// </summary>
    [HttpPatch("tracked/{savedFlightId}/toggle-notification")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleNotification(int savedFlightId, [FromQuery] string? guestId = null)
    {
        var customerId = GetCustomerId();
        if (customerId == null && string.IsNullOrEmpty(guestId))
        {
            return BadRequest(new { error = "Either authorization token or guestId must be provided" });
        }

        var (success, message, notificationEnabled) = await _trackerService.ToggleTrackedFlightNotificationAsync(savedFlightId, customerId, guestId);
        if (!success)
        {
            return NotFound(new { error = message });
        }

        return Ok(new { success, message, notificationEnabled });
    }

    /// <summary>
    /// Merge guest tracked flights to customer account upon login.
    /// </summary>
    [HttpPost("tracked/merge")]
    [Authorize(Roles = "Customer,customer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MergeFlights([FromBody] MergeFlightsRequest request)
    {
        var customerId = GetCustomerId();
        if (customerId == null)
        {
            return Unauthorized();
        }

        var (success, message) = await _trackerService.MergeGuestTrackedFlightsAsync(request.GuestId, customerId.Value);
        if (!success)
        {
            return BadRequest(new { error = message });
        }

        return Ok(new { success, message });
    }
}

public class TrackFlightRequest
{
    public string FlightIata { get; set; } = string.Empty;
    public string? GuestId { get; set; }
}

public class MergeFlightsRequest
{
    public string GuestId { get; set; } = string.Empty;
}

public class FlightTrackerGenericErrorResponse
{
    public string Error { get; set; } = string.Empty;
}
