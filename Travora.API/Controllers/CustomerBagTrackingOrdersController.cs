using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Orders.BagTracking;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.Interfaces.Services.Customer;

namespace Travora.API.Controllers;

[Route("api/v1/orders/bag-tracking")]
[ApiController]
[Authorize(Roles = "Customer,customer")]
public class CustomerBagTrackingOrdersController : ControllerBase
{
    private readonly IBagTrackingOrderService _orderService;

    public CustomerBagTrackingOrdersController(IBagTrackingOrderService orderService)
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

    [HttpPost("validate-flight")]
    [ProducesResponseType(typeof(ValidateFlightResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateFlight([FromBody] BagTrackingValidateFlightRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.ValidateFlightAsync(customerId, request, cancellationToken);
            return response.IsValid ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { IsValid = false, ErrorMessage = ex.Message });
        }
    }

    [HttpPost("validate-companion")]
    [ProducesResponseType(typeof(ValidateCompanionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateCompanion([FromForm] ValidateCompanionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.ValidateCompanionAsync(customerId, request, cancellationToken);
            return response.IsValid ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { IsValid = false, ErrorMessage = ex.Message });
        }
    }

    [HttpPost("validate-baggage")]
    [ProducesResponseType(typeof(ValidateBaggageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateBaggage(CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.ValidateBaggageAsync(customerId, cancellationToken);
            return response.IsValid ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { IsValid = false, ErrorMessage = ex.Message });
        }
    }

    [HttpPost("scan-bag")]
    [ProducesResponseType(typeof(ScanBagResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ScanBag([FromBody] ScanBagRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.ScanBagAsync(customerId, request, cancellationToken);
            return response.Found ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Found = false, ErrorMessage = ex.Message });
        }
    }

    [HttpPost("bags/{tagNumber}/photos")]
    [ProducesResponseType(typeof(UploadBagPhotosResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadBagPhotos(string tagNumber, [FromForm] List<IFormFile> photos, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.UploadBagPhotosAsync(customerId, tagNumber, photos, cancellationToken);
            return response.Saved ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Saved = false, ErrorMessage = ex.Message });
        }
    }

    [HttpGet("invoice")]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvoice(CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.GetInvoiceAsync(customerId, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ErrorMessage = ex.Message });
        }
    }

    [HttpPost("confirm")]
    [ProducesResponseType(typeof(ConfirmOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmOrder(CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.ConfirmOrderAsync(customerId, cancellationToken);
            return response.Success ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ErrorMessage = ex.Message });
        }
    }
}
