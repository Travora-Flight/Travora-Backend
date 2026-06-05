namespace Travora.Application.DTOs.Admin.Requests;

public class AvailableEmployeeDto
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Shift { get; set; } = string.Empty;
    public string? VehicleDetails { get; set; }
}
