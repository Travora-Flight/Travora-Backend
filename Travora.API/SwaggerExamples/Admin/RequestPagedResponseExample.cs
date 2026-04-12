using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Requests;

namespace Travora.API.SwaggerExamples.Admin;

public class RequestPagedResponseExample : IExamplesProvider<RequestPagedResponse>
{
    public RequestPagedResponse GetExamples()
    {
        return new RequestPagedResponse
        {
            Total = 1,
            Requests = new List<RequestListResponse>
            {
                new RequestListResponse
                {
                    OrderId = 101,
                    ClientName = "Mostafa Ahmed",
                    Type = "Door To Door",
                    Status = "In Progress",
                    AssignedEmployee = "Mohamed Ali",
                    Time = "2026-04-12 14:30"
                }
            }
        };
    }
}
