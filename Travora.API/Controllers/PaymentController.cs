using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Payments;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/payments")]
public class PaymentController : ControllerBase
{
    private readonly IPaymobService _paymobService;

    public PaymentController(IPaymobService paymobService)
    {
        _paymobService = paymobService;
    }

    [HttpPost("initiate")]
    [Authorize(Roles = "customer")]
    public async Task<IActionResult> InitiatePayment([FromBody] PaymentInitiateRequest request)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var response = await _paymobService.InitiatePaymentAsync(request.OrderId, customerId);
        return Ok(response);
    }
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook([FromQuery] string hmac, [FromBody] System.Text.Json.JsonElement payload)
    {
        try
        {
            // بنبعت الـ JSON والـ HMAC للـ Service من غير أي لعب في الداتا
            await _paymobService.HandleWebhookAsync(payload, hmac);
        }
        catch (Exception ex)
        {
            // بنسجل أي إيرور عشان نقدر نتابعه، بس لازم نرد بـ Ok لـ Paymob
            Console.WriteLine($"Webhook Error: {ex.Message}");
        }

        return Ok(); 
    }
    [HttpGet("status/{orderId}")]
    [Authorize(Roles = "customer")]
    public async Task<IActionResult> GetPaymentStatus(int orderId)
    {
        var response = await _paymobService.GetPaymentStatusAsync(orderId);
        return Ok(response);
    }

}
