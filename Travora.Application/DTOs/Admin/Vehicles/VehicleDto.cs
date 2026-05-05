using System.ComponentModel.DataAnnotations;

namespace Travora.Application.DTOs.Admin.Vehicles;

public class VehicleResponse
{
    public int VehicleId { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Color { get; set; } = string.Empty;
    public int Capacity { get; set; }
    
    public bool IsAssigned { get; set; }
    public int? AssignedToEmployeeId { get; set; }
    public string? AssignedToEmployeeName { get; set; }
}

public class CreateVehicleRequest
{
    [Required(ErrorMessage = "Plate number is required")]
    public string PlateNumber { get; set; } = string.Empty;
    [Required(ErrorMessage = "Brand is required")]
    public string Brand { get; set; } = string.Empty;
    [Required(ErrorMessage = "Model is required")]
    public string Model { get; set; } = string.Empty;
    [Range(1900, 2100, ErrorMessage = "Manufacturing year is invalid")]
    public int Year { get; set; }
    [Required(ErrorMessage = "Color is required")]
    public string Color { get; set; } = string.Empty;
    [Range(1, 100, ErrorMessage = "Capacity must be greater than 0")]
    public int Capacity { get; set; }
}

public class UpdateVehicleRequest
{
    public string? PlateNumber { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public int? Capacity { get; set; }
}
