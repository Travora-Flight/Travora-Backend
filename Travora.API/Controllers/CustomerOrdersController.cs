using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Orders;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.Interfaces.Services.Customer;

namespace Travora.API.Controllers;

[Route("api/v1/orders")]
[ApiController]
[Authorize(Roles = "Customer,customer")]
public class CustomerOrdersController : ControllerBase
{
    private readonly ICustomerOrderService _orderService;

    public CustomerOrdersController(ICustomerOrderService orderService)
    {
        _orderService = orderService;
    }

    private int GetCustomerId()
    {
        var claim = User.FindFirst("customerId");
        if (claim == null || !int.TryParse(claim.Value, out int customerId))
            throw new UnauthorizedAccessException("Customer ID missing in token.");
        return customerId;
    }

    // GET /api/v1/orders
    [HttpGet]
    [ProducesResponseType(typeof(List<OrderListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders(CancellationToken cancellationToken)
    {
        try
        {
            var customerId = GetCustomerId();
            var response = await _orderService.GetCustomerOrdersAsync(customerId, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { errorMessage = ex.Message });
        }
    }

    // GET /api/v1/orders/{orderId}
    [HttpGet("{orderId}")]
    [ProducesResponseType(typeof(OrderDetailsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrderDetails(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var customerId = GetCustomerId();
            var response = await _orderService.GetOrderDetailsAsync(customerId, orderId, cancellationToken);
            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { errorMessage = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { errorMessage = ex.Message });
        }
    }

    // PATCH /api/v1/orders/{orderId}/cancel
    [HttpPatch("{orderId}/cancel")]
    [ProducesResponseType(typeof(CancelOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelOrder(int orderId, [FromBody] CancelOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var customerId = GetCustomerId();
            var response = await _orderService.CancelOrderAsync(customerId, orderId, request.CancellationReason, cancellationToken);
            return response.Success ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { errorMessage = ex.Message });
        }
    }

    // GET /api/v1/orders/{orderId}/available-slots?type=pickup&date=2025-12-29
    [HttpGet("{orderId}/available-slots")]
    [ProducesResponseType(typeof(AvailableSlotsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableSlots(
        int orderId, [FromQuery] string type, [FromQuery] DateTime date, CancellationToken cancellationToken)
    {
        try
        {
            var customerId = GetCustomerId();
            var response = await _orderService.GetAvailableSlotsForRescheduleAsync(customerId, orderId, type, date, cancellationToken);

            if (!response.IsValid)
                return BadRequest(response);

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { errorMessage = ex.Message });
        }
    }

    // PATCH /api/v1/orders/{orderId}/reschedule
    [HttpPatch("{orderId}/reschedule")]
    [ProducesResponseType(typeof(RescheduleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RescheduleOrder(int orderId, [FromBody] RescheduleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var customerId = GetCustomerId();
            var response = await _orderService.RescheduleOrderAsync(customerId, orderId, request, cancellationToken);
            return response.Success ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { errorMessage = ex.Message });
        }
    }

    // GET /api/v1/orders/{orderId}/boarding-pass
    [HttpGet("{orderId}/boarding-pass")]
    [ProducesResponseType(typeof(BoardingPassResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBoardingPass(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var customerId = GetCustomerId();
            var response = await _orderService.GetBoardingPassAsync(customerId, orderId, cancellationToken);
            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { errorMessage = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { errorMessage = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { errorMessage = ex.Message });
        }
    }

    // GET /api/v1/orders/{orderId}/boarding-pass/download
    [HttpGet("{orderId}/boarding-pass/download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadBoardingPass(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var customerId = GetCustomerId();
            var (pdfBytes, fileName) = await _orderService.DownloadBoardingPassAsync(customerId, orderId, cancellationToken);
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { errorMessage = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { errorMessage = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { errorMessage = ex.Message });
        }
    }
}
