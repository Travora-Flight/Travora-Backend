using Microsoft.AspNetCore.Http;
using Travora.Application.DTOs.Employee.Account;

namespace Travora.Application.Interfaces.Services.Employee;

public interface IEmployeeAccountService
{
    Task<EmployeeProfileResponse> GetProfileAsync(int employeeId);
    Task<UpdateProfileResponse> UpdateProfileAsync(int employeeId, string? mobileNumber, string? address, IFormFile? profilePhoto);
    Task ChangePasswordAsync(int employeeId, string currentPassword, string newPassword, string confirmPassword);
}
