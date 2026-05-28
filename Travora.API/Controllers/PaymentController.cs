using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Payments;
using Travora.Application.Interfaces.Services;
using Travora.Infrastructure.Services;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/payments")]
public class PaymentController : ControllerBase
{
    private readonly IPaymobService _paymobService;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(IPaymobService paymobService, ILogger<PaymentController> logger)
    {
        _paymobService = paymobService;
        _logger = logger;
    }

    [HttpPost("initiate")]
    [Authorize(Roles = "customer")]
    public async Task<IActionResult> InitiatePayment([FromBody] PaymentInitiateRequest request)
    {
        var customerId = int.Parse(User.FindFirstValue("customerId")!);
        var response = await _paymobService.InitiatePaymentAsync(request.OrderId, customerId, request.PaymentMethodId);
        return Ok(response);
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook([FromQuery] string hmac, [FromBody] System.Text.Json.JsonElement payload)
    {
        try
        {
            await _paymobService.HandleWebhookAsync(payload, hmac);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook processing error");
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

    /// <summary>
    /// Diagnostic endpoint to view the last webhook payloads received from Paymob.
    /// Remove this endpoint in production.
    /// </summary>
    [HttpGet("webhook-debug")]
    [AllowAnonymous]
    public IActionResult WebhookDebug()
    {
        var webhooks = PaymobService.LastWebhooks;
        return Ok(new
        {
            count = webhooks.Count,
            message = webhooks.Count == 0 ? "No webhooks received yet. Either Paymob hasn't sent one, or the server was restarted." : null,
            webhooks
        });
    }
}
