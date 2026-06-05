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
    private readonly IPaymobService _paymobService;

    public CustomerPaymentMethodController(
        IPaymentMethodService paymentMethodService,
        IPaymobService paymobService)
    {
        _paymentMethodService = paymentMethodService;
        _paymobService = paymobService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Payments.PaymentMethodsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentMethods()
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var result = await _paymentMethodService.GetCustomerPaymentMethodsAsync(customerId);
        return Ok(result);
    }


    [HttpPost("initiate-save")]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Payments.SaveCardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> InitiateSaveCard()
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var response = await _paymobService.InitiateSaveCardAsync(customerId);
        return Ok(response);
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



public class PaymentMethodGenericResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
