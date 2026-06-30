using System.ComponentModel.DataAnnotations;

using System.Text.Json.Serialization;

namespace Travora.Application.DTOs.Admin.Vehicles;

public class VehicleEmployeeResponse
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "Active" or "Inactive"
    public string Email { get; set; } = string.Empty;
    public string ShiftType { get; set; } = string.Empty;
}

public class VehicleResponse
{
    public int VehicleId { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Color { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public bool IsActive { get; set; }
    
    public bool IsAssigned { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<VehicleEmployeeResponse>? Employees { get; set; }
}

public class CreateVehicleRequest
{
    [Required(ErrorMessage = "Plate number is required")]
    public string PlateNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Brand is required")]
    public string Brand { get; set; } = string.Empty;

    [Required(ErrorMessage = "Model is required")]
    public string Model { get; set; } = string.Empty;

    [Required(ErrorMessage = "Manufacturing year is required")]
    [Range(1900, 2100, ErrorMessage = "Manufacturing year is invalid")]
    public int? Year { get; set; }

    [Required(ErrorMessage = "Color is required")]
    public string Color { get; set; } = string.Empty;

    [Required(ErrorMessage = "Capacity is required")]
    [Range(1, 100, ErrorMessage = "Capacity must be greater than 0")]
    public int? Capacity { get; set; }

    public bool IsActive { get; set; } = true;
}

public class UpdateVehicleRequest
{
    public string? PlateNumber { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }

    [Range(1900, 2100, ErrorMessage = "Manufacturing year is invalid")]
    public int? Year { get; set; }

    public string? Color { get; set; }

    [Range(1, 100, ErrorMessage = "Capacity must be greater than 0")]
    public int? Capacity { get; set; }

    public bool? IsActive { get; set; }
}

public class VehicleStatusRequest
{
    public bool IsActive { get; set; }
}
