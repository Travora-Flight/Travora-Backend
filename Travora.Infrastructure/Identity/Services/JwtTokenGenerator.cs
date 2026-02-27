using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Travora.Application.Interfaces;
using Travora.Shared.Settings;
using Travora.Domain.Entities;
using Travora.Domain.Enums;

namespace Travora.Infrastructure.Identity.Services;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtSettings _jwtSettings;

    public JwtTokenGenerator(JwtSettings jwtSettings)
    {
        _jwtSettings = jwtSettings;
    }

    public string GenerateAdminToken(Admin admin)
    {
        var claims = new List<Claim>
        {
            new("role", "admin"),
            new("adminId", admin.AdminId.ToString()),
            new("isSuperAdmin", admin.IsSuperAdmin.ToString().ToLower()),
            new(ClaimTypes.Email, admin.Email),
            new(ClaimTypes.Name, admin.FullName)
        };

        return GenerateToken(claims);
    }

    public string GenerateEmployeeToken(Employee employee)
    {
        var claims = new List<Claim>
        {
            new("role", "employee"),
            new("employeeId", employee.EmployeeId.ToString()),
            new("jobRole", employee.JobRole.ToString()),
            new("shiftType", employee.ShiftType.ToString()),
            new(ClaimTypes.Email, employee.Email),
            new(ClaimTypes.Name, $"{employee.Firstname} {employee.Lastname}")
        };

        return GenerateToken(claims);
    }

    public string GenerateCustomerToken(Customer customer)
    {
        var claims = new List<Claim>
        {
            new("role", "customer"),
            new("customerId", customer.CustomerId.ToString()),
            new("accountStatus", customer.AccountStatus.ToString()),
            new(ClaimTypes.Email, customer.Email),
            new(ClaimTypes.Name, $"{customer.Firstname} {customer.Lastname}")
        };

        return GenerateToken(claims);
    }

    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
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
