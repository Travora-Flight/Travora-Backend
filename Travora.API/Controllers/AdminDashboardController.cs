using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.Interfaces;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/dashboard")]
[Authorize(Roles = "admin")]
public class AdminDashboardController : ControllerBase
{
    private readonly IAdminDashboardService _dashboardService;

    public AdminDashboardController(IAdminDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStatsAsync()
    {
        var result = await _dashboardService.GetDashboardStatsAsync();
        return Ok(result);
    }
}
