using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.Pricing;
using Travora.Application.Interfaces;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/pricing")]
[Authorize(Roles = "admin")]
public class AdminPricingController : ControllerBase
{
    private readonly IAdminPricingService _pricingService;

    public AdminPricingController(IAdminPricingService pricingService)
    {
        _pricingService = pricingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetPricingOverviewAsync()
    {
        var result = await _pricingService.GetPricingOverviewAsync();
        return Ok(result);
    }

    [HttpPatch("services/{serviceId}")]
    public async Task<IActionResult> UpdateServicePriceAsync(int serviceId, [FromBody] UpdateServicePriceRequest request)
    {
        await _pricingService.UpdateServicePriceAsync(serviceId, request);
        return Ok(new { success = true });
    }

    [HttpPatch("packages/{packageId}")]
    public async Task<IActionResult> UpdatePackagePricingAsync(int packageId, [FromBody] UpdatePackagePricingRequest request)
    {
        await _pricingService.UpdatePackagePricingAsync(packageId, request);
        return Ok(new { success = true });
    }
}
