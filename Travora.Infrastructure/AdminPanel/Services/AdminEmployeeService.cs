using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Employees;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.External.Communication;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;
using FluentValidation;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminEmployeeService : IAdminEmployeeService
{
    private readonly ApplicationDbContext _db;
    private readonly ICloudinaryService _cloudinary;
    private readonly IEmailService _emailService;

    public AdminEmployeeService(
        ApplicationDbContext db,
        ICloudinaryService cloudinary,
        IEmailService emailService)
    {
        _db = db;
        _cloudinary = cloudinary;
        _emailService = emailService;
    }

    public async Task<EmployeePagedResponse> GetEmployeesAsync(string? search, string? status, int page, int pageSize)
    {
        var baseQuery = _db.Employees.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            baseQuery = baseQuery.Where(e => 
                e.Firstname.ToLower().Contains(searchLower) ||
                e.Lastname.ToLower().Contains(searchLower) ||
                e.Email.ToLower().Contains(searchLower) ||
                e.PhoneNumber.Contains(searchLower));
        }

        var activeCount = await baseQuery.CountAsync(e => e.IsActive == true);
        var inactiveCount = await baseQuery.CountAsync(e => e.IsActive == false);
        var total = activeCount + inactiveCount;

        var query = baseQuery;
        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            query = query.Where(e => e.IsActive == true);
        else if (string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase))
            query = query.Where(e => e.IsActive == false);

        var employees = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmployeeListResponse
            {
                EmployeeId = e.EmployeeId,
                Name = $"{e.Firstname} {e.Lastname}",
                Mobile = e.PhoneNumber,
                Status = e.IsActive ? "active" : "inactive",
                Email = e.Email,
                ShiftType = e.ShiftType.ToString().ToLower(),
                JobRole = e.JobRole.ToString().ToLower()
            })
            .ToListAsync();

        return new EmployeePagedResponse
        {
            Employees = employees,
            ActiveCount = activeCount,
            InactiveCount = inactiveCount,
            Total = total
        };
    }

    public async Task<EmployeeProfileResponse> GetEmployeeProfileAsync(int employeeId)
    {
        var e = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        return new EmployeeProfileResponse
        {
            EmployeeId = e.EmployeeId,
            Name = $"{e.Firstname} {e.Lastname}",
            Code = $"EMP{e.EmployeeId}", // Assuming this format for now
            Status = e.IsActive ? "active" : "inactive",
            JobRole = e.JobRole.ToString().ToLower(),
            ProfileImageUrl = e.ProfileImagePath,
            NationalIdImageUrl = e.NationalIdPhotoPath,
            DriverLicenseUrl = e.DriverLicensePath,
            
            ContactInfo = new EmployeeContactInfo
            {
                Email = e.Email,
                Mobile = e.PhoneNumber
            },
            AdditionalDetails = new EmployeeAdditionalDetails
            {
                DateOfBirth = e.DateOfBirth.ToString("dd/MM/yyyy"),
                ShiftType = e.ShiftType.ToString().ToLower(),
                NationalId = e.NationalId
            },
            VehicleId = e.VehicleId,
            CheckpointId = e.CheckpointId
        };
    }

    public async Task<EmployeeFormDataResponse> GetFormDataAsync()
    {
        var activeVehicles = await _db.Vehicles
            .Where(v => !v.Employees.Any(e => e.IsActive && !e.IsDeleted))
            .Select(v => new IdNamePair { Id = v.VehicleId, DisplayName = $"{v.Brand} {v.Model} - {v.PlateNumber} ({v.Year})" })
            .ToListAsync();

        var activeCheckpoints = await _db.Checkpoints
            .Where(c => !c.Employees.Any(e => e.IsActive && !e.IsDeleted))
            .Select(c => new IdNamePair { Id = c.CheckpointId, DisplayName = c.CheckpointName })
            .ToListAsync();

        var jobRoles = Enum.GetNames(typeof(JobRole)).ToList();
        var shiftTypes = Enum.GetNames(typeof(ShiftType)).ToList();

        return new EmployeeFormDataResponse
        {
            AvailableVehicles = activeVehicles,
            AvailableCheckpoints = activeCheckpoints,
            JobRoles = jobRoles,
            ShiftTypes = shiftTypes
        };
    }

    public async Task<CreateEmployeeResponse> CreateEmployeeAsync(int adminId, CreateEmployeeRequest request)
    {
        // 1) Validation
        var validator = new Travora.Application.Validators.Admin.Employees.CreateEmployeeValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        if (await _db.Employees.AnyAsync(e => e.NationalId == request.NationalId))
            throw new InvalidOperationException("National ID already exists");

        if (request.JobRole == JobRole.Driver && request.VehicleId.HasValue)
        {
            var vehicleExists = await _db.Vehicles.AnyAsync(v => v.VehicleId == request.VehicleId);
            if (!vehicleExists) throw new KeyNotFoundException("Vehicle not found");

            var vehicleInUse = await _db.Employees.AnyAsync(e => e.VehicleId == request.VehicleId && e.IsActive && !e.IsDeleted);
            if (vehicleInUse) throw new InvalidOperationException("This vehicle is already assigned to another employee");
        }
        else if (request.JobRole == JobRole.BaggageHandler && request.CheckpointId.HasValue)
        {
            var checkpointExists = await _db.Checkpoints.AnyAsync(c => c.CheckpointId == request.CheckpointId);
            if (!checkpointExists) throw new KeyNotFoundException("Checkpoint not found");
        }

        // 2) Generate Email
        string generatedEmail = await GenerateUniqueEmailAsync(request.FirstName, request.LastName);
        
        // 3) Generate Temp Password
        string tempPassword = GenerateStrongTempPassword();
        string tempPasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

        // 4) Upload Files to Cloudinary
        string profileUrl = string.Empty, nationalIdUrl = string.Empty, licenseUrl = string.Empty;

        using (var stream = request.ProfilePhoto.OpenReadStream())
            profileUrl = await _cloudinary.UploadFileAsync(stream, request.ProfilePhoto.FileName, "travora/employees/profiles");

        using (var stream = request.NationalIdPhoto.OpenReadStream())
            nationalIdUrl = await _cloudinary.UploadFileAsync(stream, request.NationalIdPhoto.FileName, "travora/employees/national-ids");

        if (request.JobRole == JobRole.Driver && request.DriverLicense != null)
        {
            using var stream = request.DriverLicense.OpenReadStream();
            licenseUrl = await _cloudinary.UploadFileAsync(stream, request.DriverLicense.FileName, "travora/employees/licenses");
        }

        // 5) Create Entity
        var employee = new Employee
        {
            Firstname = request.FirstName,
            Lastname = request.LastName,
            PhoneNumber = request.MobileNumber,
            NationalId = request.NationalId,
            DateOfBirth = request.DateOfBirth,
            JobRole = request.JobRole,
            ShiftType = request.ShiftType,
            Email = generatedEmail,
            TempPassword = tempPasswordHash,
            PasswordHash = null,
            IsFirstLogin = true,
            ProfileImagePath = profileUrl,
            NationalIdPhotoPath = nationalIdUrl,
            DriverLicensePath = request.JobRole == JobRole.Driver ? licenseUrl : null,
            VehicleId = request.JobRole == JobRole.Driver ? request.VehicleId : null,
            CheckpointId = request.JobRole == JobRole.BaggageHandler ? request.CheckpointId : null,
            CreatedByAdminId = adminId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync(); // To get the generated EmployeeId

        // 6) Get Admin Email and Send Email
        var admin = await _db.Admins.FindAsync(adminId);
        if (admin != null)
        {
            await _emailService.SendNewEmployeeCredentialsAsync(
                adminEmail: admin.Email,
                employeeName: $"{request.FirstName} {request.LastName}",
                employeeEmail: generatedEmail,
                tempPassword: tempPassword
            );
        }

        return new CreateEmployeeResponse
        {
            Success = true,
            EmployeeId = employee.EmployeeId,
            GeneratedEmail = generatedEmail,
            TempPassword = tempPassword,
            Message = "Account created successfully. Please inform the employee of the login details."
        };
    }

    public async Task<EmployeeProfileResponse> UpdateEmployeeAsync(int employeeId, UpdateEmployeeRequest request)
    {
        var e = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        if (request.FirstName != null) e.Firstname = request.FirstName;
        if (request.LastName != null) e.Lastname = request.LastName;
        if (request.MobileNumber != null) e.PhoneNumber = request.MobileNumber;
        if (request.NationalId != null) e.NationalId = request.NationalId;
        if (request.DateOfBirth.HasValue) e.DateOfBirth = request.DateOfBirth.Value;
        if (request.JobRole.HasValue) e.JobRole = request.JobRole.Value;
        if (request.ShiftType.HasValue) e.ShiftType = request.ShiftType.Value;

        if (e.JobRole == JobRole.Driver && request.VehicleId.HasValue && request.VehicleId != e.VehicleId)
        {
            var vehicleExists = await _db.Vehicles.AnyAsync(v => v.VehicleId == request.VehicleId);
            if (!vehicleExists) throw new KeyNotFoundException("Vehicle not found");

            var vehicleInUse = await _db.Employees.AnyAsync(emp => emp.VehicleId == request.VehicleId && emp.IsActive && !emp.IsDeleted && emp.EmployeeId != employeeId);
            if (vehicleInUse) throw new InvalidOperationException("This vehicle is already assigned to another employee");
        }
        else if (e.JobRole == JobRole.BaggageHandler && request.CheckpointId.HasValue && request.CheckpointId != e.CheckpointId)
        {
            var checkpointExists = await _db.Checkpoints.AnyAsync(c => c.CheckpointId == request.CheckpointId);
            if (!checkpointExists) throw new KeyNotFoundException("Checkpoint not found");
        }

        if (request.ProfilePhoto != null)
        {
            // Optionally delete old photo:
            // if (!string.IsNullOrEmpty(e.ProfileImagePath)) await _cloudinary.DeleteFileAsync(_cloudinary.ExtractPublicId(e.ProfileImagePath));
            
            using var stream = request.ProfilePhoto.OpenReadStream();
            e.ProfileImagePath = await _cloudinary.UploadFileAsync(stream, request.ProfilePhoto.FileName, "travora/employees/profiles");
        }

        if (request.NationalIdPhoto != null)
        {
            using var stream = request.NationalIdPhoto.OpenReadStream();
            e.NationalIdPhotoPath = await _cloudinary.UploadFileAsync(stream, request.NationalIdPhoto.FileName, "travora/employees/national-ids");
        }

        if (e.JobRole == JobRole.Driver)
        {
            if (request.VehicleId.HasValue) e.VehicleId = request.VehicleId;
            if (request.DriverLicense != null)
            {
                using var stream = request.DriverLicense.OpenReadStream();
                e.DriverLicensePath = await _cloudinary.UploadFileAsync(stream, request.DriverLicense.FileName, "travora/employees/licenses");
            }
            e.CheckpointId = null; // Clear if role was changed
        }
        else if (e.JobRole == JobRole.BaggageHandler)
        {
            if (request.CheckpointId.HasValue) e.CheckpointId = request.CheckpointId;
            e.VehicleId = null;
            e.DriverLicensePath = null;
        }

        await _db.SaveChangesAsync();

        return await GetEmployeeProfileAsync(employeeId);
    }

    public async Task<bool> UpdateEmployeeStatusAsync(int employeeId, EmployeeStatusRequest request)
    {
        var e = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        e.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteEmployeeAsync(int employeeId)
    {
        var e = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        e.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    private async Task<string> GenerateUniqueEmailAsync(string firstName, string lastName)
    {
        string baseEmail = $"{firstName.ToLower().Trim()}.{lastName.ToLower().Trim()}@travora.com";
        
        bool exists = await _db.Employees.AnyAsync(e => e.Email == baseEmail);
        if (!exists) return baseEmail;

        int counter = 2;
        while (true)
        {
            string newEmail = $"{firstName.ToLower().Trim()}.{lastName.ToLower().Trim()}{counter}@travora.com";
            exists = await _db.Employees.AnyAsync(e => e.Email == newEmail);
            if (!exists) return newEmail;
            counter++;
        }
    }

    private string GenerateStrongTempPassword()
    {
        const string uppers = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lowers = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string specials = "@#$!%*?&";
        
        var rnd = new Random();
        
        var password = new char[12];
        password[0] = uppers[rnd.Next(uppers.Length)];
        password[1] = lowers[rnd.Next(lowers.Length)];
        password[2] = digits[rnd.Next(digits.Length)];
        password[3] = specials[rnd.Next(specials.Length)];
        
        string all = uppers + lowers + digits + specials;
        for (int i = 4; i < password.Length; i++)
        {
            password[i] = all[rnd.Next(all.Length)];
        }
        
        return new string(password.OrderBy(x => rnd.Next()).ToArray());
    }
}
