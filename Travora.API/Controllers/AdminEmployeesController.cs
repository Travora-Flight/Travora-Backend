using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.Employees;
using Travora.Application.Interfaces;
using System.Security.Claims;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/employees")]
[Authorize(Roles = "admin")]
public class AdminEmployeesController : ControllerBase
{
    private readonly IAdminEmployeeService _employeeService;

    public AdminEmployeesController(IAdminEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    private int GetAdminId()
    {
        var idClaim = User.FindFirst("adminId")?.Value 
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        return int.TryParse(idClaim, out var adminId) ? adminId : 0;
    }

    [HttpGet]
    [ProducesResponseType(typeof(EmployeePagedResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployeesAsync([FromQuery] string? search, [FromQuery] Travora.Domain.Enums.EmployeeFilterStatus status = Travora.Domain.Enums.EmployeeFilterStatus.Active, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _employeeService.GetEmployeesAsync(search, status.ToString(), page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(EmployeeProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployeeProfileAsync(int id)
    {
        var result = await _employeeService.GetEmployeeProfileAsync(id);
        return Ok(result);
    }

    [HttpGet("form-data")]
    [ProducesResponseType(typeof(EmployeeFormDataResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFormDataAsync()
    {
        var result = await _employeeService.GetFormDataAsync();
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateEmployeeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateEmployeeAsync([FromForm] CreateEmployeeRequest request)
    {
        var adminId = GetAdminId();
        if (adminId == 0) return Unauthorized();

        var result = await _employeeService.CreateEmployeeAsync(adminId, request);
        return Ok(result);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(EmployeeProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEmployeeAsync(int id, [FromForm] UpdateEmployeeRequest request)
    {
        var result = await _employeeService.UpdateEmployeeAsync(id, request);
        return Ok(result);
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatusAsync(int id, [FromBody] EmployeeStatusRequest request)
    {
        await _employeeService.UpdateEmployeeStatusAsync(id, request);
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEmployeeAsync(int id)
    {
        await _employeeService.DeleteEmployeeAsync(id);
        return Ok(new { success = true });
    }
}
