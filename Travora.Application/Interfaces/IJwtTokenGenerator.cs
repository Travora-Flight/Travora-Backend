using Travora.Domain.Entities;

namespace Travora.Application.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateAdminToken(Admin admin);
    string GenerateEmployeeToken(Employee employee);
    string GenerateCustomerToken(Customer customer);
    string GenerateRefreshToken();
}
