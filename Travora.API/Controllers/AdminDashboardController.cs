using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.Dashboard;
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
    [ProducesResponseType(typeof(DashboardStatsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatsAsync()
    {
        var result = await _dashboardService.GetDashboardStatsAsync();
        return Ok(result);
    }

    [HttpGet("employees/online")]
    [ProducesResponseType(typeof(OnlineEmployeesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOnlineEmployeesAsync()
    {
        var result = await _dashboardService.GetOnlineEmployeesAsync();
        return Ok(result);
    }

    [HttpGet("orders/recent")]
    [ProducesResponseType(typeof(RecentOrdersResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentOrdersAsync([FromQuery] int take = 10)
    {
        var result = await _dashboardService.GetRecentOrdersAsync(take);
        return Ok(result);
    }

    [HttpGet("employees/live-locations")]
    [ProducesResponseType(typeof(LiveLocationsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLiveLocationsAsync()
    {
        var result = await _dashboardService.GetLiveLocationsAsync();
        return Ok(result);
    }
}
