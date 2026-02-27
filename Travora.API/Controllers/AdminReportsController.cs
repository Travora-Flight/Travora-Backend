using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.Interfaces;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/reports")]
[Authorize(Roles = "admin")]
public class AdminReportsController : ControllerBase
{
    private readonly IAdminReportService _reportService;

    public AdminReportsController(IAdminReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardReportsAsync([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var result = await _reportService.GetDashboardReportsAsync(startDate, endDate);
        return Ok(result);
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrderReportsAsync([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string? status)
    {
        var result = await _reportService.GetOrderReportsAsync(startDate, endDate, status);
        return Ok(result);
    }

    [HttpGet("employees-performance")]
    public async Task<IActionResult> GetEmployeePerformanceAsync([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var result = await _reportService.GetEmployeePerformanceAsync(startDate, endDate);
        return Ok(result);
    }
}
