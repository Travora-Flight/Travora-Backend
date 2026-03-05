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
    public async Task<IActionResult> GetProfile()
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _accountService.GetProfileAsync(employeeId);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromForm] UpdateProfileRequest request)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _accountService.UpdateProfileAsync(employeeId, request.MobileNumber, request.Address, request.ProfilePhoto);
        return Ok(response);
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] EmployeeChangePasswordRequest request)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        await _accountService.ChangePasswordAsync(employeeId, request.CurrentPassword, request.NewPassword, request.ConfirmPassword);
        return Ok(new { success = true, message = "Password changed successfully" });
    }
}
