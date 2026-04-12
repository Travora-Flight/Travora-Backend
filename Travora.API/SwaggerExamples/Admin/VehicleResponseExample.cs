using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Vehicles;

namespace Travora.API.SwaggerExamples.Admin;

public class VehicleResponseExample : IExamplesProvider<VehicleResponse>
{
    public VehicleResponse GetExamples()
    {
        return new VehicleResponse
        {
            VehicleId = 1,
            PlateNumber = "ABC-123",
            Brand = "Toyota",
            Model = "Hiace",
            Year = 2022,
            Color = "White",
            Capacity = 14,
            IsAssigned = true,
            AssignedToEmployeeId = 4,
            AssignedToEmployeeName = "Salem Salem"
        };
    }
}
