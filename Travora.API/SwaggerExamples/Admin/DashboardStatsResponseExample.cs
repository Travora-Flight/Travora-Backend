using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Dashboard;

namespace Travora.API.SwaggerExamples.Admin;

public class DashboardStatsResponseExample : IExamplesProvider<DashboardStatsResponse>
{
    public DashboardStatsResponse GetExamples()
    {
        return new DashboardStatsResponse
        {
            AllEmployees = 150,
            AllEmployeesGrowth = 5,
            NewRequests = 45,
            NewRequestsGrowth = 12,
            CurrentRequests = 20,
            CurrentRequestsChange = -2,
            DoneRequests = 300,
            DoneRequestsGrowth = 15,
            WeeklyActivity = new List<WeeklyActivityItem>
            {
                new WeeklyActivityItem { Day = "Monday", Completed = 20, NewRequests = 25, Ongoing = 5 },
                new WeeklyActivityItem { Day = "Tuesday", Completed = 22, NewRequests = 30, Ongoing = 8 }
            }
        };
    }
}
