using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.Interfaces;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/dashboard")]
[Authorize(Roles = "admin")]
[Tags("AdminPricing")]
public class AdminPricingController : ControllerBase
{
    private readonly IAdminPricingService _pricingService;

    public AdminPricingController(IAdminPricingService pricingService)
    {
        _pricingService = pricingService;
    }

    [HttpGet("Pricing")]
    public async Task<IActionResult> GetDashboardPricingStatsAsync()
    {
        var overview = await _pricingService.GetPricingOverviewAsync();
        return Ok(new { stats = overview.Stats });
    }
}
