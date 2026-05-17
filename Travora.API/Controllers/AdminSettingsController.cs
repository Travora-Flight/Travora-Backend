using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.Settings;
using Travora.Application.Interfaces;
using System.Security.Claims;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/settings")]
[Authorize(Roles = "admin")]
public class AdminSettingsController : ControllerBase
{
    private readonly IAdminSettingsService _settingsService;

    public AdminSettingsController(IAdminSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    private int GetAdminId()
    {
        var idClaim = User.FindFirst("adminId")?.Value 
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        return int.TryParse(idClaim, out var adminId) ? adminId : 0;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AppSettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettingsAsync()
    {
        var result = await _settingsService.GetSettingsAsync();
        return Ok(result);
    }

    [HttpPut("general")]
    [ProducesResponseType(typeof(AppSettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateGeneralSettingsAsync([FromBody] UpdateGeneralSettingsRequest request)
    {
        var result = await _settingsService.UpdateGeneralSettingsAsync(request);
        return Ok(result);
    }

    [HttpPut("tracking")]
    [ProducesResponseType(typeof(AppSettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTrackingSettingsAsync([FromBody] UpdateTrackingSettingsRequest request)
    {
        var result = await _settingsService.UpdateTrackingSettingsAsync(request);
        return Ok(result);
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePasswordAsync([FromBody] ChangePasswordRequest request)
    {
        var adminId = GetAdminId();
        if (adminId == 0) return Unauthorized();

        await _settingsService.ChangePasswordAsync(adminId, request);
        return Ok(new { success = true, message = "Password changed successfully" });
    }
}
