using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Employee.Account;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Application.Interfaces.Services.Employee;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.EmployeePanel.Services;

public class EmployeeAccountService : IEmployeeAccountService
{
    private readonly ApplicationDbContext _db;
    private readonly ICloudinaryService _cloudinary;

    public EmployeeAccountService(ApplicationDbContext db, ICloudinaryService cloudinary)
    {
        _db = db;
        _cloudinary = cloudinary;
    }

    public async Task<EmployeeProfileResponse> GetProfileAsync(int employeeId)
    {
        var employee = await _db.Employees
            .Include(e => e.Checkpoint)
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        return new EmployeeProfileResponse
        {
            ProfileImageUrl = string.IsNullOrEmpty(employee.ProfileImagePath) ? null : employee.ProfileImagePath,
            FirstName = employee.Firstname,
            LastName = employee.Lastname,
            JobRole = employee.JobRole.ToString(),
            ShiftType = employee.ShiftType.ToString(),
            MobileNumber = employee.PhoneNumber,
            Email = employee.Email,
            NationalId = employee.NationalId,
            DateOfBirth = employee.DateOfBirth.ToString("dd/MM/yyyy"),
            CheckPoint = employee.Checkpoint?.CheckpointName,
            VehicleId = employee.VehicleId
        };
    }

    public async Task<UpdateProfileResponse> UpdateProfileAsync(int employeeId, string? mobileNumber, string? address, IFormFile? profilePhoto)
    {
        var employee = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        if (mobileNumber != null)
            employee.PhoneNumber = mobileNumber;

        string? photoUrl = null;
        if (profilePhoto != null)
        {
            using var stream = profilePhoto.OpenReadStream();
            photoUrl = await _cloudinary.UploadFileAsync(stream, profilePhoto.FileName, "travora/employees/profiles");
            employee.ProfileImagePath = photoUrl;
        }

        employee.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new UpdateProfileResponse
        {
            Success = true,
            ProfileImageUrl = photoUrl ?? employee.ProfileImagePath,
            MobileNumber = employee.PhoneNumber,
            Address = address
        };
    }

    public async Task ChangePasswordAsync(int employeeId, string currentPassword, string newPassword, string confirmPassword)
    {
        var employee = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        if (string.IsNullOrEmpty(employee.PasswordHash) || !BCrypt.Net.BCrypt.Verify(currentPassword, employee.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect");

        if (newPassword != confirmPassword)
            throw new ArgumentException("Passwords do not match");

        if (currentPassword == newPassword)
            throw new ArgumentException("New password must be different from current password");

        // Password strength validation
        if (newPassword.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters long");
        if (!newPassword.Any(char.IsUpper))
            throw new ArgumentException("Password must contain at least one uppercase letter");
        if (!newPassword.Any(char.IsLower))
            throw new ArgumentException("Password must contain at least one lowercase letter");
        if (!newPassword.Any(char.IsDigit))
            throw new ArgumentException("Password must contain at least one number");
        if (!newPassword.Any(c => !char.IsLetterOrDigit(c)))
            throw new ArgumentException("Password must contain at least one special character (!@#$%^&*)");

        employee.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        employee.IsFirstLogin = false;
        employee.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
