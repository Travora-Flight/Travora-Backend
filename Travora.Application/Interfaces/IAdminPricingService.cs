using Travora.Application.DTOs.Admin.Pricing;
using Travora.Domain.Enums;

namespace Travora.Application.Interfaces;

public interface IAdminPricingService
{
    Task<PricingOverviewResponse> GetPricingOverviewAsync();

    Task<List<ServiceDetailResponse>> GetServicesAsync(ActivationFilterStatus status);
    Task<object> CreateServiceAsync(CreateServiceRequest request);
    Task<bool> UpdateServiceAsync(int serviceId, UpdateServiceRequest request);
    Task<bool> DeleteServiceAsync(int serviceId);

    Task<List<PackageDetailResponse>> GetPackagesAsync(ActivationFilterStatus status);
    Task<object> CreatePackageAsync(CreatePackageRequest request);
    Task<bool> UpdatePackageAsync(int packageId, UpdatePackageRequest request);
    Task<bool> DeletePackageAsync(int packageId);
}
