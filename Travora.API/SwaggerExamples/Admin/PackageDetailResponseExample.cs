using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Pricing;

namespace Travora.API.SwaggerExamples.Admin;

public class PackageDetailResponseExample : IExamplesProvider<PackageDetailResponse>
{
    public PackageDetailResponse GetExamples()
    {
        return new PackageDetailResponse
        {
            PackageId = 1,
            Code = "PKG-001",
            PackageName = "VIP Departure",
            IsActive = true,
            TotalPrice = 1500m,
            FinalPrice = 1200m,
            Discount = 20m,
            Currency = "EGP",
            IncludedCompanions = 2,
            ExtraCompanionPrice = 200m,
            IncludedBags = 4,
            ExtraBagPrice = 100m,
            ServicesIncluded = new List<PackageServiceDetail>
            {
                new PackageServiceDetail { Name = "Fast Track", Phase = "Pre-Flight", IsFree = true },
                new PackageServiceDetail { Name = "Lounge Access", Phase = "Pre-Flight", IsFree = true }
            }
        };
    }
}
