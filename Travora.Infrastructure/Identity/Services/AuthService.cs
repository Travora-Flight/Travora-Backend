using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Auth;
using Travora.Application.Interfaces;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;
using Travora.Shared.Settings;

namespace Travora.Infrastructure.Identity.Services;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _db;
    private readonly IJwtTokenGenerator _jwt;
    private readonly JwtSettings _jwtSettings;

    public AuthService(ApplicationDbContext db, IJwtTokenGenerator jwt, JwtSettings jwtSettings)
    {
        _db = db;
        _jwt = jwt;
        _jwtSettings = jwtSettings;
    }

    public async Task<AuthResponse> LoginAdminAsync(string email, string password, string ipAddress, string userAgent)
    {
        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Email == email);

        if (admin == null)
        {
            await LogLogin(null, null, null, UserType.Admin, LoginStatus.Failed, "Invalid credentials", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash))
        {
            await LogLogin(admin.AdminId, null, null, UserType.Admin, LoginStatus.Failed, "Invalid credentials", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        if (!admin.IsActive)
        {
            await LogLogin(admin.AdminId, null, null, UserType.Admin, LoginStatus.Failed, "Account inactive", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Account inactive");
        }

        await LogLogin(admin.AdminId, null, null, UserType.Admin, LoginStatus.Success, null, ipAddress, userAgent);

        admin.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var accessToken = _jwt.GenerateAdminToken(admin);
        var refreshToken = await CreateRefreshToken(admin.AdminId, UserType.Admin);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = 86400,
            Role = "admin",
            UserData = new
            {
                adminId = admin.AdminId,
                fullName = admin.FullName,
                isSuperAdmin = admin.IsSuperAdmin
            }
        };
    }

    public async Task<AuthResponse> LoginEmployeeAsync(string email, string password, string ipAddress, string userAgent)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Email == email);

        if (employee == null)
        {
            await LogLogin(null, null, null, UserType.Employee, LoginStatus.Failed, "Invalid credentials", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        if (employee.IsFirstLogin)
        {
            if (string.IsNullOrEmpty(employee.TempPassword) || !BCrypt.Net.BCrypt.Verify(password, employee.TempPassword))
            {
                await LogLogin(null, null, employee.EmployeeId, UserType.Employee, LoginStatus.Failed, "Invalid credentials", ipAddress, userAgent);
                throw new UnauthorizedAccessException("Invalid credentials");
            }
        }
        else
        {
            if (string.IsNullOrEmpty(employee.PasswordHash) || !BCrypt.Net.BCrypt.Verify(password, employee.PasswordHash))
            {
                await LogLogin(null, null, employee.EmployeeId, UserType.Employee, LoginStatus.Failed, "Invalid credentials", ipAddress, userAgent);
                throw new UnauthorizedAccessException("Invalid credentials");
            }
        }

        if (!employee.IsActive)
        {
            await LogLogin(null, null, employee.EmployeeId, UserType.Employee, LoginStatus.Failed, "Account inactive", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Account inactive");
        }

        await LogLogin(null, null, employee.EmployeeId, UserType.Employee, LoginStatus.Success, null, ipAddress, userAgent);

        var accessToken = _jwt.GenerateEmployeeToken(employee);
        var refreshToken = await CreateRefreshToken(employee.EmployeeId, UserType.Employee);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = 28800, // 8 hours for employees (shift-based)
            Role = "employee",
            UserData = new
            {
                employeeId = employee.EmployeeId,
                firstName = employee.Firstname,
                jobRole = employee.JobRole.ToString(),
                shiftType = employee.ShiftType.ToString(),
                isFirstLogin = employee.IsFirstLogin
            }
        };
    }

    public async Task<AuthResponse> LoginCustomerAsync(string email, string password, string ipAddress, string userAgent)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == email);

        if (customer == null)
        {
            await LogLogin(null, null, null, UserType.Customer, LoginStatus.Failed, "Invalid credentials", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, customer.PasswordHash))
        {
            await LogLogin(null, customer.CustomerId, null, UserType.Customer, LoginStatus.Failed, "Invalid credentials", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        if (!customer.IsActive)
        {
            await LogLogin(null, customer.CustomerId, null, UserType.Customer, LoginStatus.Failed, "Account inactive", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Account inactive");
        }

        if (!customer.EmailVerified)
        {
            await LogLogin(null, customer.CustomerId, null, UserType.Customer, LoginStatus.Failed, "Email not verified", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Please verify your email first.");
        }

        await LogLogin(null, customer.CustomerId, null, UserType.Customer, LoginStatus.Success, null, ipAddress, userAgent);

        customer.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var accessToken = _jwt.GenerateCustomerToken(customer);
        var refreshToken = await CreateRefreshToken(customer.CustomerId, UserType.Customer);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = 86400,
            Role = "customer",
            UserData = new
            {
                customerId = customer.CustomerId,
                firstName = customer.Firstname,
                accountStatus = customer.AccountStatus.ToString()
            }
        };
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken)
    {
        var storedToken = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (storedToken == null)
            throw new UnauthorizedAccessException("Invalid token");

        if (storedToken.IsRevoked)
            throw new UnauthorizedAccessException("Token revoked");

        if (storedToken.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Token expired");

        storedToken.IsRevoked = true;

        string accessToken;
        string role;
        object? userData;

        switch (storedToken.UserType)
        {
            case UserType.Admin:
                var admin = await _db.Admins.FindAsync(storedToken.UserId)
                    ?? throw new UnauthorizedAccessException("User not found");
                accessToken = _jwt.GenerateAdminToken(admin);
                role = "admin";
                userData = new { adminId = admin.AdminId, fullName = admin.FullName, isSuperAdmin = admin.IsSuperAdmin };
                break;

            case UserType.Employee:
                var employee = await _db.Employees.FindAsync(storedToken.UserId)
                    ?? throw new UnauthorizedAccessException("User not found");
                accessToken = _jwt.GenerateEmployeeToken(employee);
                role = "employee";
                userData = new { employeeId = employee.EmployeeId, firstName = employee.Firstname, jobRole = employee.JobRole.ToString(), shiftType = employee.ShiftType.ToString(), isFirstLogin = employee.IsFirstLogin };
                break;

            case UserType.Customer:
                var customer = await _db.Customers.FindAsync(storedToken.UserId)
                    ?? throw new UnauthorizedAccessException("User not found");
                accessToken = _jwt.GenerateCustomerToken(customer);
                role = "customer";
                userData = new { customerId = customer.CustomerId, firstName = customer.Firstname, accountStatus = customer.AccountStatus.ToString() };
                break;

            default:
                throw new UnauthorizedAccessException("Invalid user type");
        }

        var newRefreshToken = await CreateRefreshToken(storedToken.UserId, storedToken.UserType);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresIn = role == "employee" ? 28800 : 86400, // 8h for employees, 24h for others
            Role = role,
            UserData = userData
        };
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var storedToken = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);
        if (storedToken != null)
        {
            storedToken.IsRevoked = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<object> ChangePasswordFirstLoginAsync(int employeeId, string tempPassword, string newPassword, string confirmPassword)
    {
        var employee = await _db.Employees.FindAsync(employeeId)
            ?? throw new UnauthorizedAccessException("Employee not found");

        if (!employee.IsFirstLogin)
            throw new InvalidOperationException("Password already changed");

        if (string.IsNullOrEmpty(employee.TempPassword) || !BCrypt.Net.BCrypt.Verify(tempPassword, employee.TempPassword))
            throw new UnauthorizedAccessException("Temp password incorrect");

        if (newPassword != confirmPassword)
            throw new ArgumentException("Passwords do not match");

        employee.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        employee.TempPassword = null;
        employee.IsFirstLogin = false;
        employee.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return new { success = true, message = "Password changed successfully" };
    }

    // ===== Private Helpers =====

    private async Task<string> CreateRefreshToken(int userId, UserType userType)
    {
        var token = _jwt.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
            UserId = userId,
            UserType = userType
        });

        await _db.SaveChangesAsync();
        return token;
    }

    private async Task LogLogin(int? adminId, int? customerId, int? employeeId, UserType userType, LoginStatus status, string? failureReason, string ipAddress, string userAgent)
    {
        _db.LoginLogs.Add(new LoginLog
        {
            AdminId = adminId,
            CustomerId = customerId,
            EmployeeId = employeeId,
            UserType = userType,
            LoginStatus = status,
            FailureReason = failureReason ?? string.Empty,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceType = DetectDeviceType(userAgent)
        });

        await _db.SaveChangesAsync();
    }

    private static string DetectDeviceType(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return "Unknown";
        var ua = userAgent.ToLower();
        if (ua.Contains("mobile") || ua.Contains("android") || ua.Contains("iphone")) return "Mobile";
        if (ua.Contains("tablet") || ua.Contains("ipad")) return "Tablet";
        if (ua.Contains("postman")) return "Postman";
        if (ua.Contains("curl")) return "CLI";
        if (ua.Contains("mozilla") || ua.Contains("chrome") || ua.Contains("safari") || ua.Contains("edge")) return "Desktop";
        return "Unknown";
    }
}
