using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Account;

namespace Travora.API.SwaggerExamples.Admin;

public class AdminAccountResponseExample : IExamplesProvider<AdminAccountResponse>
{
    public AdminAccountResponse GetExamples()
    {
        return new AdminAccountResponse
        {
            AdminId = 1,
            FullName = "Administrator User",
            Email = "admin@travora.com",
            Phone = "+201000000000",
            IsSuperAdmin = true
        };
    }
}
