using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/airports")]
[AllowAnonymous]
public class AirportController : ControllerBase
{
    private readonly IAirportDetailsService _airportDetailsService;

    public AirportController(IAirportDetailsService airportDetailsService)
    {
        _airportDetailsService = airportDetailsService;
    }

    [HttpGet("{icaoCode}/details")]
    public async Task<IActionResult> GetDetails(string icaoCode)
    {
        try
        {
            var result = await _airportDetailsService.GetAirportDetailsAsync(icaoCode.ToUpper());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
