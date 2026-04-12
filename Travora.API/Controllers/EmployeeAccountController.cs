using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Employee.Account;
using Travora.Application.Interfaces.Services.Employee;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/employee/account")]
[Authorize(Roles = "employee")]
public class EmployeeAccountController : ControllerBase
{
    private readonly IEmployeeAccountService _accountService;

    public EmployeeAccountController(IEmployeeAccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(EmployeeProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile()
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _accountService.GetProfileAsync(employeeId);
        return Ok(response);
    }

    [HttpPut]
    [ProducesResponseType(typeof(UpdateProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile([FromForm] UpdateProfileRequest request)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _accountService.UpdateProfileAsync(employeeId, request.MobileNumber, request.Address, request.ProfilePhoto);
        return Ok(response);
    }

    [HttpPost("change-password")]
    [ProducesResponseType(typeof(EmployeeChangePasswordResponseWrapper), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] EmployeeChangePasswordRequest request)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        await _accountService.ChangePasswordAsync(employeeId, request.CurrentPassword, request.NewPassword, request.ConfirmPassword);
        return Ok(new EmployeeChangePasswordResponseWrapper { Success = true, Message = "Password changed successfully" });
    }
}

public class EmployeeChangePasswordResponseWrapper
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
