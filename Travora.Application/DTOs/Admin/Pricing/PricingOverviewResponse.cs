namespace Travora.Application.DTOs.Admin.Pricing;

public class PricingOverviewResponse
{
    public PricingStatsResponse Stats { get; set; } = new();
    public List<ServiceDetailResponse> Services { get; set; } = new();
    public List<PackageDetailResponse> Packages { get; set; } = new();
}
