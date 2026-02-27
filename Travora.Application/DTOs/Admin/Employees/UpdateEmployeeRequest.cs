using Microsoft.AspNetCore.Http;
using Travora.Domain.Enums;

namespace Travora.Application.DTOs.Admin.Employees;

public class UpdateEmployeeRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MobileNumber { get; set; }
    public string? NationalId { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public JobRole? JobRole { get; set; }
    public ShiftType? ShiftType { get; set; }

    public IFormFile? ProfilePhoto { get; set; }
    public IFormFile? NationalIdPhoto { get; set; }

    // Driver specific
    public int? VehicleId { get; set; }
    public IFormFile? DriverLicense { get; set; }

    // Baggage Handler specific
    public int? CheckpointId { get; set; }
}
