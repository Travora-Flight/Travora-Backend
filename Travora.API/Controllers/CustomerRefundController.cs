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



    [HttpGet("{orderId}/refund")]
    [ProducesResponseType(typeof(RefundStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CustomerRefundGenericResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRefundStatus(int orderId)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var response = await _refundService.GetRefundStatusAsync(customerId, orderId);
        if (response == null)
            return NotFound(new CustomerRefundGenericResponse { Message = "No refund request found for this order" });
        return Ok(response);
    }
}

public class CustomerRefundGenericResponse
{
    public string Message { get; set; } = string.Empty;
}
