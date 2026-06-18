using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Flights.Tracker;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/flights")]
[AllowAnonymous]
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
}

public class FlightTrackerGenericErrorResponse
{
    public string Error { get; set; } = string.Empty;
}
