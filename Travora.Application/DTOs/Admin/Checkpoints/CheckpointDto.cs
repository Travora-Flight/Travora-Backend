using System.ComponentModel.DataAnnotations;
using Travora.Domain.Enums;

namespace Travora.Application.DTOs.Admin.Checkpoints;

public class CheckpointResponse
{
    public int CheckpointId { get; set; }
    public string CheckpointName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? GpsLatitude { get; set; }
    public decimal? GpsLongitude { get; set; }
    public bool IsAssigned { get; set; }
}

public class CheckpointEmployeeResponse
{
    public int EmployeeId { get; set; }
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? ProfileImagePath { get; set; }
    public string ShiftType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class CreateCheckpointRequest
{
    [Required(ErrorMessage = "Checkpoint name is required")]
    public string CheckpointName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Checkpoint type is required")]
    public string CheckpointType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SequenceOrder { get; set; }
    public decimal? GpsLatitude { get; set; }
    public decimal? GpsLongitude { get; set; }
    public int? AirportId { get; set; }
}

public class UpdateCheckpointRequest
{
    public string? CheckpointName { get; set; }
    public string? CheckpointType { get; set; }
    public string? Description { get; set; }
    public int? SequenceOrder { get; set; }
    public decimal? GpsLatitude { get; set; }
    public decimal? GpsLongitude { get; set; }
    public int? AirportId { get; set; }
}
