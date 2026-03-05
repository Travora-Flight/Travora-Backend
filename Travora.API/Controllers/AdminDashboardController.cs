using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.Interfaces;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "admin")]
public class AdminDashboardController : ControllerBase
{
    private readonly IAdminDashboardService _dashboardService;

    public AdminDashboardController(IAdminDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("dashboard/stats")]
    public async Task<IActionResult> GetStatsAsync()
    {
        var result = await _dashboardService.GetDashboardStatsAsync();
        return Ok(result);
    }

    [HttpGet("employees/online")]
    public async Task<IActionResult> GetOnlineEmployeesAsync()
    {
        var result = await _dashboardService.GetOnlineEmployeesAsync();
        return Ok(result);
    }

    [HttpGet("orders/recent")]
    public async Task<IActionResult> GetRecentOrdersAsync([FromQuery] int take = 10)
    {
        var result = await _dashboardService.GetRecentOrdersAsync(take);
        return Ok(result);
    }

    [HttpGet("employees/live-locations")]
    public async Task<IActionResult> GetLiveLocationsAsync()
    {
        var result = await _dashboardService.GetLiveLocationsAsync();
        return Ok(result);
    }
}
