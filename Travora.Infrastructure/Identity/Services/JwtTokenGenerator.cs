using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Travora.Shared.Settings;
using Travora.Domain.Entities;
using Travora.Domain.Enums;

namespace Travora.Infrastructure.Identity.Services;

public class JwtTokenGenerator
{
    private readonly JwtSettings _jwtSettings;

    public JwtTokenGenerator(JwtSettings jwtSettings)
    {
        _jwtSettings = jwtSettings;
    }

    // توليد توكن للعميل
    public string GenerateCustomerToken(Customer customer)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, customer.CustomerId.ToString()),
            new(ClaimTypes.Email, customer.Email),
            new(ClaimTypes.Name, $"{customer.Firstname} {customer.Lastname}"),
            new(ClaimTypes.Role, UserType.Customer.ToString()),
            new("PassportNumber", customer.PassportNumber),
            new("UserType", UserType.Customer.ToString())
        };

        return GenerateToken(claims);
    }

    // توليد توكن للموظف
    public string GenerateEmployeeToken(Employee employee)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, employee.EmployeeId.ToString()),
            new(ClaimTypes.Email, employee.Email),
            new(ClaimTypes.Name, $"{employee.Firstname} {employee.Lastname}"),
            new(ClaimTypes.Role, UserType.Employee.ToString()),
            new("JobRole", employee.JobRole.ToString()),
            new("UserType", UserType.Employee.ToString())
        };

        return GenerateToken(claims);
    }

    // توليد توكن للأدمن
    public string GenerateAdminToken(Admin admin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.AdminId.ToString()),
            new(ClaimTypes.Email, admin.Email),
            new(ClaimTypes.Name, admin.FullName),
            new(ClaimTypes.Role, UserType.Admin.ToString()),
            new("UserType", UserType.Admin.ToString())
        };

        return GenerateToken(claims);
    }

    private string GenerateToken(List<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(_jwtSettings.ExpiryDays),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
