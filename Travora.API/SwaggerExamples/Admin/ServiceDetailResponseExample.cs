using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Pricing;

namespace Travora.API.SwaggerExamples.Admin;

public class ServiceDetailResponseExample : IExamplesProvider<ServiceDetailResponse>
{
    public ServiceDetailResponse GetExamples()
    {
        return new ServiceDetailResponse
        {
            ServiceId = 1,
            Code = "SRV-LOU",
            Name = "Lounge Access",
            Type = "Addon",
            Description = "Premium lounge access before departure",
            BasePrice = 300m,
            Currency = "EGP",
            IsActive = true
        };
    }
}
