using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.LiveTracker;
using Travora.Application.Interfaces;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/employees/last-locations")]
[Authorize(Roles = "admin")]
public class AdminLiveTrackerController : ControllerBase
{
    private readonly IAdminLiveTrackerService _liveTrackerService;

    public AdminLiveTrackerController(IAdminLiveTrackerService liveTrackerService)
    {
        _liveTrackerService = liveTrackerService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(LiveEmployeeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLastLocationsAsync([FromQuery] string? filter, [FromQuery] string? search)
    {
        var result = await _liveTrackerService.GetLastLocationsAsync(filter, search);
        return Ok(result);
    }

    [HttpGet("~/api/v1/admin/employees/{employeeId}/location-details")]
    [ProducesResponseType(typeof(EmployeeLocationDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployeeLocationDetailsAsync(int employeeId)
    {
        var result = await _liveTrackerService.GetEmployeeLocationDetailsAsync(employeeId);
        return Ok(result);
    }
}
