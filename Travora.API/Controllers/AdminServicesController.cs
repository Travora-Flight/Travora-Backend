using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.Pricing;
using Travora.Application.Interfaces;
using Travora.Domain.Enums;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/services")]
[Authorize(Roles = "admin")]
[Tags("AdminPricing")]
public class AdminServicesController : ControllerBase
{
    private readonly IAdminPricingService _pricingService;

    public AdminServicesController(IAdminPricingService pricingService)
    {
        _pricingService = pricingService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<ServiceDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetServicesAsync([FromQuery] ActivationFilterStatus status = ActivationFilterStatus.All)
    {
        var result = await _pricingService.GetServicesAsync(status);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateServiceAsync([FromBody] CreateServiceRequest request)
    {
        var result = await _pricingService.CreateServiceAsync(request);
        return Ok(result);
    }

    [HttpPut("{serviceId}")]
    public async Task<IActionResult> UpdateServiceAsync(int serviceId, [FromBody] UpdateServiceRequest request)
    {
        await _pricingService.UpdateServiceAsync(serviceId, request);
        return Ok(new { success = true });
    }

    [HttpDelete("{serviceId}")]
    public async Task<IActionResult> DeleteServiceAsync(int serviceId)
    {
        await _pricingService.DeleteServiceAsync(serviceId);
        return Ok(new { success = true });
    }
}
