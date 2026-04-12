using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.Interfaces.Services.Employee;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/employee")]
[Authorize(Roles = "employee")]
public class EmployeeDashboardController : ControllerBase
{
    private readonly IEmployeeDashboardService _dashboardService;

    public EmployeeDashboardController(IEmployeeDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Employee.Dashboard.EmployeeDashboardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard()
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _dashboardService.GetDashboardAsync(employeeId);
        return Ok(response);
    }
}
