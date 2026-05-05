using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Orders.CarService;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.Interfaces.Services;
using Travora.Application.Interfaces.Services.Customer;

namespace Travora.API.Controllers;

[Route("api/v1/orders/car-service")]
[ApiController]
[Authorize(Roles = "Customer,customer")]
public class CustomerCarServiceOrdersController : ControllerBase
{
    private readonly ICarServiceOrderService _orderService;
    private readonly IDraftOrderService _draftOrderService;

    public CustomerCarServiceOrdersController(ICarServiceOrderService orderService, IDraftOrderService draftOrderService)
    {
        _orderService = orderService;
        _draftOrderService = draftOrderService;
    }

    private int GetCustomerId()
    {
        var claim = User.FindFirst("customerId");
        if (claim == null || !int.TryParse(claim.Value, out int customerId))
            throw new UnauthorizedAccessException("Customer ID missing in token.");
        return customerId;
    }

    // ===== STEP 1 — Validate Flight =====
    [HttpPost("validate-flight")]
    [ProducesResponseType(typeof(CarServiceValidateFlightResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateFlight([FromBody] CarServiceValidateFlightRequest request, CancellationToken cancellationToken)
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

    // ===== STEP 2 — Validate Companion =====
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

    // ===== STEP 2.5 — Validate Baggage =====
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

    // ===== STEP 3 — Resolve Location =====
    [HttpPost("resolve-location")]
    [ProducesResponseType(typeof(ResolveLocationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveLocation([FromBody] CarServiceResolveLocationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.ResolveLocationAsync(customerId, request, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ErrorMessage = ex.Message });
        }
    }

    // ===== STEP 4 — Available Slots =====
    [HttpGet("available-slots")]
    [ProducesResponseType(typeof(AvailableSlotsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableSlots([FromQuery] DateTime date, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.GetAvailableSlotsAsync(customerId, date, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ErrorMessage = ex.Message });
        }
    }

    // ===== STEP 4.5 — Select Slot =====
    [HttpPost("select-slot")]
    [ProducesResponseType(typeof(SelectSlotResponseWrapper), StatusCodes.Status200OK)]
    public async Task<IActionResult> SelectSlot([FromBody] SelectSlotRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();

            var slotsResponse = await _orderService.GetAvailableSlotsAsync(customerId, request.Date, cancellationToken);
            var chosenSlot = slotsResponse.AvailableSlots.FirstOrDefault(s => s.Slot == request.Slot);

            if (chosenSlot == null || !chosenSlot.Available)
                return BadRequest(new { success = false, errorMessage = "This time slot is not available" });

            var draft = await _draftOrderService.GetCarServiceDraftAsync(customerId.ToString(), cancellationToken);
            if (draft == null)
                return BadRequest(new { success = false, errorMessage = "Session not found" });

            draft.SelectedSlot = request.Slot;
            draft.SelectedSlotDate = request.Date;
            await _draftOrderService.SaveCarServiceDraftAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

            return Ok(new SelectSlotResponseWrapper { Success = true, SelectedSlot = request.Slot, Date = request.Date });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { errorMessage = ex.Message });
        }
    }

    // ===== STEP 5 — My Bags (delivery_from_airport only) =====
    [HttpGet("my-bags")]
    [ProducesResponseType(typeof(MyBagsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyBags(CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.GetMyBagsAsync(customerId, cancellationToken);
            if (!string.IsNullOrEmpty(response.ErrorMessage))
                return BadRequest(response);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ErrorMessage = ex.Message });
        }
    }

    // ===== STEP 5.5 — Select Bags =====
    [HttpPost("select-bags")]
    [ProducesResponseType(typeof(SelectBagsResponseWrapper), StatusCodes.Status200OK)]
    public async Task<IActionResult> SelectBags([FromBody] SelectBagsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            await _orderService.SelectBagsAsync(customerId, request, cancellationToken);
            return Ok(new SelectBagsResponseWrapper { Success = true, SelectedCount = request.SelectedTagNumbers.Count });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ErrorMessage = ex.Message });
        }
    }

    // ===== STEP 6 — Invoice =====
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

    // ===== STEP 7 — Confirm =====
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

public class SelectBagsResponseWrapper
{
    public bool Success { get; set; }
    public int SelectedCount { get; set; }
}
