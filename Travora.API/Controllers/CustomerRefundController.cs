using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Refunds;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/orders")]
[Authorize(Roles = "customer")]
public class CustomerRefundController : ControllerBase
{
    private readonly IRefundService _refundService;

    public CustomerRefundController(IRefundService refundService)
    {
        _refundService = refundService;
    }

    [HttpPost("{orderId}/refund")]
    public async Task<IActionResult> RequestRefund(int orderId, [FromBody] RefundRequest request)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var response = await _refundService.RequestRefundAsync(customerId, orderId, request);
        return Ok(response);
    }

    [HttpGet("{orderId}/refund")]
    public async Task<IActionResult> GetRefundStatus(int orderId)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var response = await _refundService.GetRefundStatusAsync(customerId, orderId);
        if (response == null)
            return NotFound(new { message = "لا يوجد طلب استرداد لهذا الأوردر" });
        return Ok(response);
    }
}
