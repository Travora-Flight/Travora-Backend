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

    public CustomerPaymentMethodController(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    [HttpGet]
    public async Task<IActionResult> GetPaymentMethods()
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var result = await _paymentMethodService.GetCustomerPaymentMethodsAsync(customerId);
        return Ok(result);
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
