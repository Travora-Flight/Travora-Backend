using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Dashboard;

namespace Travora.API.SwaggerExamples.Admin;

public class LiveLocationsResponseExample : IExamplesProvider<LiveLocationsResponse>
{
    public LiveLocationsResponse GetExamples()
    {
        return new LiveLocationsResponse
        {
            ActiveCount = 1,
            Drivers = new List<LiveDriverItem>
            {
                new LiveDriverItem
                {
                    EmployeeId = 4,
                    Name = "Salem Salem",
                    Code = "DRV-004",
                    Latitude = 30.123m,
                    Longitude = 31.456m,
                    Status = "busy",
                    CurrentTask = "Heading to Airport",
                    SpeedKmh = 65.5m,
                    IsMoving = true,
                    LastUpdated = "Just now"
                }
            }
        };
    }
}
