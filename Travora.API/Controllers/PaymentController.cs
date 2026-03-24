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
    public async Task<IActionResult> Webhook()
    {
        var formData = new Dictionary<string, string>();

        var contentType = Request.ContentType ?? "";

        if (contentType.Contains("application/json"))
        {
            // Paymob بيبعت JSON
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            
            var json = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
            
            // استخرج الـ obj
            if (json.TryGetProperty("obj", out var obj))
                FlattenJsonElement(obj, formData, "");
            
            // استخرج الـ hmac من الـ query string
        }
        else
        {
            // form data
            foreach (var key in Request.Form.Keys)
                formData[key] = Request.Form[key].ToString();

            if (Request.Form.ContainsKey("obj"))
            {
                var objJson = Request.Form["obj"].ToString();
                try
                {
                    var obj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(objJson);
                    FlattenJsonElement(obj, formData, "");
                }
                catch { }
            }
        }

        var hmac = Request.Query.ContainsKey("hmac")
            ? Request.Query["hmac"].ToString()
            : formData.GetValueOrDefault("hmac", "");

        try
        {
            await _paymobService.HandleWebhookAsync(formData, hmac);
        }
        catch { }

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
    /// Flattens a JSON element into dot-notation keys for HMAC calculation
    /// </summary>
    private static void FlattenJsonElement(System.Text.Json.JsonElement element, Dictionary<string, string> dict, string prefix)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    FlattenJsonElement(prop.Value, dict, key);
                }
                break;
            case System.Text.Json.JsonValueKind.Array:
                break; // Skip arrays for HMAC
            default:
                if (!dict.ContainsKey(prefix))
                    dict[prefix] = element.ToString();
                break;
        }
    }
}
