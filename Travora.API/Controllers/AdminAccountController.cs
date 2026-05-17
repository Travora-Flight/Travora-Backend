using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.Account;
using Travora.Application.DTOs.Admin.Settings;
using Travora.Application.Interfaces;
using System.Security.Claims;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/account")]
[Authorize(Roles = "admin")]
public class AdminAccountController : ControllerBase
{
    private readonly IAdminAccountService _accountService;

    public AdminAccountController(IAdminAccountService accountService)
    {
        _accountService = accountService;
    }

    private int GetAdminId()
    {
        var idClaim = User.FindFirst("adminId")?.Value 
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        return int.TryParse(idClaim, out var adminId) ? adminId : 0;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AdminAccountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccountAsync()
    {
        var adminId = GetAdminId();
        if (adminId == 0) return Unauthorized();

        var result = await _accountService.GetAccountDetailsAsync(adminId);
        return Ok(result);
    }

    [HttpPut]
    [ProducesResponseType(typeof(AdminAccountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAccountAsync([FromBody] UpdateAdminAccountRequest request)
    {
        var adminId = GetAdminId();
        if (adminId == 0) return Unauthorized();

        var result = await _accountService.UpdateAccountAsync(adminId, request);
        return Ok(result);
    }
}
