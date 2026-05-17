namespace Travora.Application.DTOs.Admin.Employees;

public class VehiclesShiftsAvailabilityResponse
{
    public List<AvailableVehicleDto> AvailableVehicles { get; set; } = new();
    public List<string> ShiftTypes { get; set; } = new();
}

public class AvailableVehicleDto
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public List<string> FreeShifts { get; set; } = new();
}
