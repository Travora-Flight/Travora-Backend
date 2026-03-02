using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Employee : IHasTimestamps, ISoftDelete
{
    public int EmployeeId { get; set; }
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? TempPassword { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string NationalId { get; set; } = string.Empty;
    public JobRole JobRole { get; set; }
    public string ProfileImagePath { get; set; } = string.Empty;
    public string NationalIdPhotoPath { get; set; } = string.Empty;
    public string? DriverLicensePath { get; set; }
    public DateTime HireDate { get; set; } = DateTime.UtcNow;
    public ShiftType ShiftType { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public bool IsFirstLogin { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int CreatedByAdminId { get; set; }
    public int? CheckpointId { get; set; }
    public int? VehicleId { get; set; }

    // Navigation properties
    public Admin CreatedByAdmin { get; set; } = null!;
    public Checkpoint? Checkpoint { get; set; }
    public Vehicle? Vehicle { get; set; }
    public ICollection<DriverTracking> DriverTrackings { get; set; } = new List<DriverTracking>();
    public ICollection<OrderService> AssignedOrderServices { get; set; } = new List<OrderService>();
    public ICollection<SecurityLock> AppliedSecurityLocks { get; set; } = new List<SecurityLock>();
    public ICollection<QrScan> QrScans { get; set; } = new List<QrScan>();
    public ICollection<BaggagePhoto> BaggagePhotos { get; set; } = new List<BaggagePhoto>();
    public ICollection<BaggageTracking> BaggageTrackings { get; set; } = new List<BaggageTracking>();
    public ICollection<LoginLog> LoginLogs { get; set; } = new List<LoginLog>();
}
