using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Refunds;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/refunds")]
[Authorize(Roles = "admin")]
public class AdminRefundController : ControllerBase
{
    private readonly IRefundService _refundService;

    public AdminRefundController(IRefundService refundService)
    {
        _refundService = refundService;
    }

    private int GetAdminId()
    {
        var idClaim = User.FindFirst("adminId")?.Value
            ?? User.FindFirst("sub")?.Value;
        return int.TryParse(idClaim, out var adminId) ? adminId : 0;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllRefunds()
    {
        var result = await _refundService.GetAllRefundsAsync();
        return Ok(result);
    }

    [HttpGet("{refundId}")]
    public async Task<IActionResult> GetRefundDetail(int refundId)
    {
        var result = await _refundService.GetRefundDetailAsync(refundId);
        return Ok(result);
    }

    [HttpPost("{refundId}/approve")]
    public async Task<IActionResult> ApproveRefund(int refundId)
    {
        var adminId = GetAdminId();
        if (adminId == 0) return Unauthorized();
        var result = await _refundService.ApproveRefundAsync(adminId, refundId);
        return Ok(result);
    }

    [HttpPost("{refundId}/reject")]
    public async Task<IActionResult> RejectRefund(int refundId, [FromBody] AdminProcessRefundRequest request)
    {
        var adminId = GetAdminId();
        if (adminId == 0) return Unauthorized();
        var result = await _refundService.RejectRefundAsync(adminId, refundId, request);
        return Ok(result);
    }
}
