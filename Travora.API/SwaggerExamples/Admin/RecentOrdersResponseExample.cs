using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Dashboard;

namespace Travora.API.SwaggerExamples.Admin;

public class RecentOrdersResponseExample : IExamplesProvider<RecentOrdersResponse>
{
    public RecentOrdersResponse GetExamples()
    {
        return new RecentOrdersResponse
        {
            Orders = new List<RecentOrderItem>
            {
                new RecentOrderItem
                {
                    OrderId = 101,
                    ClientName = "Sarah Connor",
                    Type = "Door To Door",
                    Status = "In Progress",
                    StatusCode = "in_progress",
                    EmployeeName = "Ahmed Ali",
                    Time = "14:30",
                    Date = "2026-04-12"
                },
                new RecentOrderItem
                {
                    OrderId = 102,
                    ClientName = "John Smith",
                    Type = "Car Service",
                    Status = "New",
                    StatusCode = "new",
                    EmployeeName = null,
                    Time = "15:45",
                    Date = "2026-04-12"
                }
            }
        };
    }
}
