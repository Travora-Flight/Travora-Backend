using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.Pricing;
using Travora.Application.Interfaces;
using Travora.Domain.Enums;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/packages")]
[Authorize(Roles = "admin")]
[Tags("AdminPricing")]
public class AdminPackagesController : ControllerBase
{
    private readonly IAdminPricingService _pricingService;

    public AdminPackagesController(IAdminPricingService pricingService)
    {
        _pricingService = pricingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetPackagesAsync([FromQuery] ActivationFilterStatus status = ActivationFilterStatus.All)
    {
        var result = await _pricingService.GetPackagesAsync(status);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePackageAsync([FromBody] CreatePackageRequest request)
    {
        var result = await _pricingService.CreatePackageAsync(request);
        return Ok(result);
    }

    [HttpPut("{packageId}")]
    public async Task<IActionResult> UpdatePackageAsync(int packageId, [FromBody] UpdatePackageRequest request)
    {
        await _pricingService.UpdatePackageAsync(packageId, request);
        return Ok(new { success = true });
    }

    [HttpDelete("{packageId}")]
    public async Task<IActionResult> DeletePackageAsync(int packageId)
    {
        await _pricingService.DeletePackageAsync(packageId);
        return Ok(new { success = true });
    }
}
