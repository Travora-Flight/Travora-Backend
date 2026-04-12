using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.Passport;
using Travora.Application.Interfaces;
using System.Security.Claims;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/passport-verifications")]
[Authorize(Roles = "admin")]
public class AdminPassportController : ControllerBase
{
    private readonly IAdminPassportService _passportService;

    public AdminPassportController(IAdminPassportService passportService)
    {
        _passportService = passportService;
    }

    private int GetAdminId()
    {
        var idClaim = User.FindFirst("adminId")?.Value 
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        return int.TryParse(idClaim, out var adminId) ? adminId : 0;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PassportVerificationListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPassportVerificationsAsync([FromQuery] string? status)
    {
        var result = await _passportService.GetPassportVerificationsAsync(status);
        return Ok(result);
    }

    [HttpPost("{documentId}/approve")]
    public async Task<IActionResult> ApprovePassportAsync(int documentId, [FromBody] ApprovePassportRequest request)
    {
        var adminId = GetAdminId();
        if (adminId == 0) return Unauthorized();

        await _passportService.ApprovePassportAsync(documentId, adminId, request);
        return Ok(new { success = true });
    }

    [HttpPost("{documentId}/reject")]
    public async Task<IActionResult> RejectPassportAsync(int documentId, [FromBody] RejectPassportRequest request)
    {
        var adminId = GetAdminId();
        if (adminId == 0) return Unauthorized();

        await _passportService.RejectPassportAsync(documentId, adminId, request);
        return Ok(new { success = true });
    }
}
