using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.Interfaces.Services.Customer;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Controllers;

[Route("api/v1/orders/door-to-door")]
[ApiController]
[Authorize(Roles = "Customer,customer")] // Role might be lowercase based on JwtTokenGenerator
public class CustomerDoorToDoorOrdersController : ControllerBase
{
    private readonly IDoorToDoorOrderService _orderService;
    private readonly IDraftOrderService _draftOrderService;

    public CustomerDoorToDoorOrdersController(IDoorToDoorOrderService orderService, IDraftOrderService draftOrderService)
    {
        _orderService = orderService;
        _draftOrderService = draftOrderService;
    }

    private int GetCustomerId()
    {
        var claim = User.FindFirst("customerId");
        if (claim == null || !int.TryParse(claim.Value, out int customerId))
        {
            throw new UnauthorizedAccessException("Customer ID missing in token.");
        }
        return customerId;
    }

    [HttpPost("validate-flight")]
    [ProducesResponseType(typeof(ValidateFlightResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateFlight([FromBody] ValidateFlightRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.ValidateFlightAsync(customerId, request, cancellationToken);
            
            if (!response.IsValid)
            {
                return BadRequest(response); // returning the DTO with ErrorMessage
            }

            return Ok(response);
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
            
            if (!response.IsValid)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { IsValid = false, ErrorMessage = ex.Message });
        }
    }

    [HttpPost("validate-baggage")]
    [ProducesResponseType(typeof(DoorToDoorValidateBaggageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateBaggage(CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.ValidateBaggageAsync(customerId, cancellationToken);
            
            if (!response.IsValid)
            {
                return BadRequest(response); // returns ErrorCode and ErrorMessage
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { IsValid = false, ErrorMessage = ex.Message });
        }
    }

    [HttpPost("resolve-location")]
    [ProducesResponseType(typeof(ResolveLocationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveLocation([FromBody] ResolveLocationRequest request, CancellationToken cancellationToken)
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

    [HttpPatch("update-location")]
    [ProducesResponseType(typeof(ResolveLocationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateLocationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.UpdateLocationAsync(customerId, request, cancellationToken);
            return response.IsValid ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { IsValid = false, ErrorMessage = ex.Message });
        }
    }
    
    [HttpGet("available-pickup-dates")]
    [ProducesResponseType(typeof(AvailableDatesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailablePickupDates(CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.GetAvailablePickupDatesAsync(customerId, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { IsValid = false, ErrorMessage = ex.Message });
        }
    }

    [HttpGet("available-slots")]
    [ProducesResponseType(typeof(AvailableSlotsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableSlots(
        [FromQuery] DateTime date,
        CancellationToken cancellationToken)
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

    [HttpPost("select-slot")]
    [ProducesResponseType(typeof(SelectSlotResponseWrapper), StatusCodes.Status200OK)]
    public async Task<IActionResult> SelectSlot([FromBody] SelectSlotRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            
            // Check if the slot is actually available
            var slotsResponse = await _orderService.GetAvailableSlotsAsync(
                customerId, request.Date, cancellationToken);
            
            var chosenSlot = slotsResponse.AvailableSlots
                .FirstOrDefault(s => s.Slot == request.Slot);
            
            if (chosenSlot == null || !chosenSlot.Available)
                return BadRequest(new { success = false, errorMessage = "This time slot is not available" });

            // Save to Draft
            var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
            if (draft == null)
                return BadRequest(new { success = false, errorMessage = "Draft not found" });

            draft.SelectedSlot = request.Slot;
            draft.SelectedSlotDate = request.Date;
            await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

            return Ok(new SelectSlotResponseWrapper { Success = true, SelectedSlot = request.Slot, Date = request.Date });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { errorMessage = ex.Message });
        }
    }

    [HttpGet("available-delivery-dates")]
    [ProducesResponseType(typeof(AvailableDatesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableDeliveryDates(CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.GetAvailableDeliveryDatesAsync(customerId, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { IsValid = false, ErrorMessage = ex.Message });
        }
    }

    [HttpGet("available-delivery-slots")]
    [ProducesResponseType(typeof(AvailableSlotsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableDeliverySlots(
        [FromQuery] DateTime date,
        CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.GetAvailableDeliverySlotsAsync(customerId, date, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ErrorMessage = ex.Message });
        }
    }

    [HttpPost("select-delivery-slot")]
    [ProducesResponseType(typeof(SelectDeliverySlotResponseWrapper), StatusCodes.Status200OK)]
    public async Task<IActionResult> SelectDeliverySlot([FromBody] SelectSlotRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            
            // Check if the slot is actually available
            var slotsResponse = await _orderService.GetAvailableDeliverySlotsAsync(
                customerId, request.Date, cancellationToken);
            
            var chosenSlot = slotsResponse.AvailableSlots
                .FirstOrDefault(s => s.Slot == request.Slot);
            
            if (chosenSlot == null || !chosenSlot.Available)
                return BadRequest(new { success = false, errorMessage = "This time slot is not available" });

            // Save to Draft
            var draft = await _draftOrderService.GetDraftOrderAsync(customerId.ToString(), cancellationToken);
            if (draft == null)
                return BadRequest(new { success = false, errorMessage = "Draft not found" });

            draft.SelectedDeliverySlot = request.Slot;
            draft.SelectedDeliverySlotDate = request.Date;
            await _draftOrderService.SaveDraftOrderAsync(draft, TimeSpan.FromMinutes(30), cancellationToken);

            return Ok(new SelectDeliverySlotResponseWrapper { Success = true, SelectedDeliverySlot = request.Slot, Date = request.Date });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { errorMessage = ex.Message });
        }
    }

    [HttpPost("customs")]
    [ProducesResponseType(typeof(SetCustomsTypeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetCustomsType([FromBody] SetCustomsTypeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.SetCustomsTypeAsync(customerId, request, cancellationToken);
            return response.Success ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ErrorMessage = ex.Message });
        }
    }

    [HttpGet("customs/categories")]
    [ProducesResponseType(typeof(List<CustomsCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCustomsCategories(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _orderService.GetCustomsCategoriesAsync(cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ErrorMessage = ex.Message });
        }
    }

    [HttpPost("customs/items")]
    [ProducesResponseType(typeof(AddCustomsItemResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddCustomsItem([FromForm] AddCustomsItemRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int customerId = GetCustomerId();
            var response = await _orderService.AddCustomsItemAsync(customerId, request, cancellationToken);
            return response.Success ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ErrorMessage = ex.Message });
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

public class SelectSlotResponseWrapper
{
    public bool Success { get; set; }
    public string SelectedSlot { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}

public class SelectDeliverySlotResponseWrapper
{
    public bool Success { get; set; }
    public string SelectedDeliverySlot { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}
