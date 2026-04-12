using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Employee.Location;
using Travora.Application.Interfaces.Services.Employee;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/employee")]
[Authorize(Roles = "employee")]
public class EmployeeLocationController : ControllerBase
{
    private readonly IEmployeeLocationService _locationService;

    public EmployeeLocationController(IEmployeeLocationService locationService)
    {
        _locationService = locationService;
    }

    [HttpPost("location")]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Employee.Location.DriverLocationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLocation([FromBody] DriverLocationRequest request)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _locationService.UpdateLocationAsync(employeeId, request);
        return Ok(response);
    }
}
