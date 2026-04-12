using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/customer/payment-methods")]
[Authorize(Roles = "customer")]
public class CustomerPaymentMethodController : ControllerBase
{
    private readonly IPaymentMethodService _paymentMethodService;
    private readonly Travora.Application.Interfaces.Services.Customer.ICustomerProfileService _profileService;

    public CustomerPaymentMethodController(
        IPaymentMethodService paymentMethodService,
        Travora.Application.Interfaces.Services.Customer.ICustomerProfileService profileService)
    {
        _paymentMethodService = paymentMethodService;
        _profileService = profileService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Payments.PaymentMethodsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentMethods()
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var result = await _paymentMethodService.GetCustomerPaymentMethodsAsync(customerId);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(AddPaymentMethodResponseWrapper), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPaymentMethod([FromBody] Travora.Application.DTOs.Customer.Profile.AddPaymentMethodRequest request)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var (success, message, data) = await _profileService.AddPaymentMethodAsync(customerId, request);
        
        if (!success)
            return BadRequest(new { success, message });

        return Ok(new AddPaymentMethodResponseWrapper { Success = success, Message = message, Data = data });
    }

    [HttpPost("{id}/set-default")]
    [ProducesResponseType(typeof(PaymentMethodGenericResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDefault(int id)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var success = await _paymentMethodService.SetDefaultPaymentMethodAsync(customerId, id);
        if (!success)
            return NotFound(new PaymentMethodGenericResponse { Success = false, Message = "Card not found" });
        return Ok(new PaymentMethodGenericResponse { Success = true, Message = "Card set as default successfully" });
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(PaymentMethodGenericResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(int id)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var (success, message) = await _paymentMethodService.DeletePaymentMethodAsync(customerId, id);
        if (!success)
            return BadRequest(new PaymentMethodGenericResponse { Success = false, Message = message });
        return Ok(new PaymentMethodGenericResponse { Success = true, Message = message });
    }
}

public class AddPaymentMethodResponseWrapper
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; } // Could be explicitly typed if we know the DTO
}

public class PaymentMethodGenericResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
