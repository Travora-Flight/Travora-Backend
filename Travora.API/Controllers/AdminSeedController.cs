using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.Interfaces.Services.Admin;

namespace Travora.API.Controllers;

[Route("api/v1/admin/seed")]
[ApiController]
[Authorize(Roles = "admin")]
public class AdminSeedController : ControllerBase
{
    private readonly IAviationSeederService _seederService;

    public AdminSeedController(IAviationSeederService seederService)
    {
        _seederService = seederService;
    }

    [HttpPost("countries")]
    public async Task<IActionResult> SeedCountries()
    {
        var result = await _seederService.SeedCountriesAsync();
        if (!result.Success) return StatusCode(500, result);
        return Ok(result);
    }

    [HttpPost("cities")]
    public async Task<IActionResult> SeedCities()
    {
        var result = await _seederService.SeedCitiesAsync();
        if (!result.Success) return StatusCode(500, result);
        return Ok(result);
    }

    [HttpPost("airports")]
    public async Task<IActionResult> SeedAirports()
    {
        var result = await _seederService.SeedAirportsAsync();
        if (!result.Success) return StatusCode(500, result);
        return Ok(result);
    }

    [HttpPost("airlines")]
    public async Task<IActionResult> SeedAirlines()
    {
        var result = await _seederService.SeedAirlinesAsync();
        if (!result.Success) return StatusCode(500, result);
        return Ok(result);
    }

    [HttpPost("aircraft")]
    public async Task<IActionResult> SeedAircraft()
    {
        var result = await _seederService.SeedAircraftAsync();
        if (!result.Success) return StatusCode(500, result);
        return Ok(result);
    }
}
