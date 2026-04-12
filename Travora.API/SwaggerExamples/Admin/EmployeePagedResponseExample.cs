using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Employees;

namespace Travora.API.SwaggerExamples.Admin;

public class EmployeePagedResponseExample : IExamplesProvider<EmployeePagedResponse>
{
    public EmployeePagedResponse GetExamples()
    {
        return new EmployeePagedResponse
        {
            Total = 2,
            ActiveCount = 1,
            InactiveCount = 1,
            Employees = new List<EmployeeListResponse>
            {
                new EmployeeListResponse
                {
                    EmployeeId = 1,
                    Name = "Ahmed Ali",
                    Mobile = "+201001234567",
                    Status = "Active",
                    Email = "ahmed.ali@travora.com",
                    ShiftType = "Morning",
                    JobRole = "Driver"
                },
                new EmployeeListResponse
                {
                    EmployeeId = 2,
                    Name = "Omar Hassan",
                    Mobile = "+201112223344",
                    Status = "Inactive",
                    Email = "omar.hassan@travora.com",
                    ShiftType = "Night",
                    JobRole = "Baggage Handler"
                }
            }
        };
    }
}
