using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/flights")]
[AllowAnonymous]
public class FlightTrackerController : ControllerBase
{
    private readonly IFlightTrackerService _trackerService;

    public FlightTrackerController(IFlightTrackerService trackerService)
    {
        _trackerService = trackerService;
    }

    /// <summary>
    /// Get live flights for the map. Optional viewport filtering with lat/lng/distance.
    /// </summary>
    [HttpGet("live")]
    public async Task<IActionResult> GetLiveFlights(
        [FromQuery] decimal? lat = null,
        [FromQuery] decimal? lng = null,
        [FromQuery] int? distance = null)
    {
        var result = await _trackerService.GetLiveFlightsAsync(lat, lng, distance);
        return Ok(result);
    }

    /// <summary>
    /// Search for flights and airports by query string.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return BadRequest(new { error = "Search query must be at least 2 characters" });

        var result = await _trackerService.SearchAsync(q.Trim());
        return Ok(result);
    }

    /// <summary>
    /// Get full flight details including trail, schedule, and aircraft info.
    /// </summary>
    [HttpGet("{flightIata}/details")]
    public async Task<IActionResult> GetFlightDetails(string flightIata)
    {
        var result = await _trackerService.GetFlightDetailsAsync(flightIata.Trim().ToUpper());

        if (result == null)
            return NotFound(new { error = "Flight not found" });

        return Ok(result);
    }
}
