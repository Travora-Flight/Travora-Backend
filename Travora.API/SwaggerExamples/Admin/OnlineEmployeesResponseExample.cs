using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Dashboard;

namespace Travora.API.SwaggerExamples.Admin;

public class OnlineEmployeesResponseExample : IExamplesProvider<OnlineEmployeesResponse>
{
    public OnlineEmployeesResponse GetExamples()
    {
        return new OnlineEmployeesResponse
        {
            OnlineCount = 2,
            Employees = new List<OnlineEmployeeDetail>
            {
                new OnlineEmployeeDetail
                {
                    EmployeeId = 1,
                    Name = "Ahmed Ali",
                    Code = "EMP-001",
                    ProfileImageUrl = "https://example.com/avatar1.jpg",
                    Latitude = 30.123m,
                    Longitude = 31.456m,
                    Status = "available",
                    CurrentTask = null,
                    LastUpdated = "2 mins ago"
                },
                new OnlineEmployeeDetail
                {
                    EmployeeId = 2,
                    Name = "Omar Hassan",
                    Code = "EMP-002",
                    ProfileImageUrl = "https://example.com/avatar2.jpg",
                    Latitude = 29.987m,
                    Longitude = 32.123m,
                    Status = "busy",
                    CurrentTask = "Baggage Delivery #1203",
                    LastUpdated = "Just now"
                }
            }
        };
    }
}
