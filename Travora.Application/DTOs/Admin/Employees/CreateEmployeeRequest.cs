using Microsoft.AspNetCore.Http;
using Travora.Domain.Enums;

namespace Travora.Application.DTOs.Admin.Employees;

public class CreateEmployeeRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public string NationalId { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public JobRole JobRole { get; set; }
    public ShiftType ShiftType { get; set; }

    public IFormFile ProfilePhoto { get; set; } = null!;
    public IFormFile NationalIdPhoto { get; set; } = null!;

    // Driver specific
    public int? VehicleId { get; set; }
    public IFormFile? DriverLicense { get; set; }

    // Baggage Handler specific
    public int? CheckpointId { get; set; }
}
