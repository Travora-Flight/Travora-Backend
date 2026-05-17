using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Employees;

namespace Travora.API.SwaggerExamples.Admin;

public class VehiclesShiftsAvailabilityResponseExample : IExamplesProvider<VehiclesShiftsAvailabilityResponse>
{
    public VehiclesShiftsAvailabilityResponse GetExamples()
    {
        return new VehiclesShiftsAvailabilityResponse
        {
            AvailableVehicles = new List<AvailableVehicleDto>
            {
                new AvailableVehicleDto 
                { 
                    Id = 1, 
                    DisplayName = "Toyota Hiace (Plate: ABC-123)",
                    FreeShifts = new List<string> { "Morning", "Evening", "Night", "rotating" }
                },
                new AvailableVehicleDto 
                { 
                    Id = 2, 
                    DisplayName = "Nissan Urvan (Plate: XYZ-789)",
                    FreeShifts = new List<string> { "Evening", "Night" }
                }
            },
            ShiftTypes = new List<string> { "Morning", "Evening", "Night", "rotating" }
        };
    }
}
