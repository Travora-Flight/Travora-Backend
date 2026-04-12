using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Employees;

namespace Travora.API.SwaggerExamples.Admin;

public class CreateEmployeeResponseExample : IExamplesProvider<CreateEmployeeResponse>
{
    public CreateEmployeeResponse GetExamples()
    {
        return new CreateEmployeeResponse
        {
            Success = true,
            EmployeeId = 15,
            GeneratedEmail = "john.doe5@travora.com",
            TempPassword = "Travora#User123",
            Message = "Employee created successfully. Please provide them with these credentials."
        };
    }
}
