using Swashbuckle.AspNetCore.Filters;
using Travora.API.Controllers;
using Travora.Application.DTOs.Admin.Pricing;

namespace Travora.API.SwaggerExamples.Admin;

public class PricingDashboardResponseExample : IExamplesProvider<PricingDashboardResponse>
{
    public PricingDashboardResponse GetExamples()
    {
        return new PricingDashboardResponse
        {
            Stats = new PricingStatsResponse
            {
                TotalServices = 12,
                TotalPackages = 5,
                ActiveServices = 10
            }
        };
    }
}
