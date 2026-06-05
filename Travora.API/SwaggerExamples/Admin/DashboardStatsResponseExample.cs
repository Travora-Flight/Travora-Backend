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
            NewRequests = 45,
            CurrentRequests = 20,
            DoneRequests = 300,
            WeeklyActivity = new List<WeeklyActivityItem>
            {
                new WeeklyActivityItem { Day = "Monday", Completed = 20, NewRequests = 25, Ongoing = 5 },
                new WeeklyActivityItem { Day = "Tuesday", Completed = 22, NewRequests = 30, Ongoing = 8 }
            }
        };
    }
}
