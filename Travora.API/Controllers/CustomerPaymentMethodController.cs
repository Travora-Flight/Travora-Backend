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
    public async Task<IActionResult> GetPaymentMethods()
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var result = await _paymentMethodService.GetCustomerPaymentMethodsAsync(customerId);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> AddPaymentMethod([FromBody] Travora.Application.DTOs.Customer.Profile.AddPaymentMethodRequest request)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var (success, message, data) = await _profileService.AddPaymentMethodAsync(customerId, request);
        
        if (!success)
            return BadRequest(new { success, message });

        return Ok(new { success, message, data });
    }

    [HttpPost("{id}/set-default")]
    public async Task<IActionResult> SetDefault(int id)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var success = await _paymentMethodService.SetDefaultPaymentMethodAsync(customerId, id);
        if (!success)
            return NotFound(new { message = "الكارت مش موجود" });
        return Ok(new { success = true, message = "تم تعيين الكارت كافتراضي" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var (success, message) = await _paymentMethodService.DeletePaymentMethodAsync(customerId, id);
        if (!success)
            return BadRequest(new { success = false, message });
        return Ok(new { success = true, message });
    }
}
