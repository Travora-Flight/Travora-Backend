using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Employees;

namespace Travora.API.SwaggerExamples.Admin;

public class EmployeeFormDataResponseExample : IExamplesProvider<EmployeeFormDataResponse>
{
    public EmployeeFormDataResponse GetExamples()
    {
        return new EmployeeFormDataResponse
        {
            AvailableVehicles = new List<IdNamePair>
            {
                new IdNamePair { Id = 1, DisplayName = "Toyota Hiace (Plate: ABC-123)" },
                new IdNamePair { Id = 2, DisplayName = "Nissan Urvan (Plate: XYZ-789)" }
            },
            AvailableCheckpoints = new List<IdNamePair>
            {
                new IdNamePair { Id = 10, DisplayName = "Security Scanner 1" },
                new IdNamePair { Id = 11, DisplayName = "Check-in Desk A" }
            },
            JobRoles = new List<string> { "Driver", "Baggage Handler", "Agent" },
            ShiftTypes = new List<string> { "Morning", "Night", "Rotating" }
        };
    }
}
