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
    public async Task<IActionResult> GetPassportVerificationsAsync(
        [FromQuery] Travora.Domain.Enums.PassportVerificationStatusFilter status = Travora.Domain.Enums.PassportVerificationStatusFilter.Pending,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? searchTerm = null)
    {
        var result = await _passportService.GetPassportVerificationsAsync(status, pageNumber, pageSize, searchTerm);
        return Ok(result);
    }

    [HttpGet("counts")]
    [ProducesResponseType(typeof(PassportVerificationCountsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPassportVerificationCountsAsync()
    {
        var result = await _passportService.GetPassportVerificationCountsAsync();
        return Ok(result);
    }

    [HttpGet("{documentId}")]
    [ProducesResponseType(typeof(PassportVerificationDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPassportVerificationDetailsAsync(int documentId)
    {
        var result = await _passportService.GetPassportVerificationDetailsAsync(documentId);
        if (result == null) return NotFound(new { message = "Passport verification request not found." });
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
