using Travora.Application.DTOs.Admin.Pricing;

namespace Travora.Application.Interfaces;

public interface IAdminPricingService
{
    Task<PricingOverviewResponse> GetPricingOverviewAsync();
    Task<bool> UpdateServicePriceAsync(int serviceId, UpdateServicePriceRequest request);
    Task<bool> UpdatePackagePricingAsync(int packageId, UpdatePackagePricingRequest request);
}
