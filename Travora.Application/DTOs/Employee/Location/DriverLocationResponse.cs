namespace Travora.Application.DTOs.Employee.Location;

public class DriverLocationResponse
{
    public bool Success { get; set; }
    public bool SavedToDb { get; set; }
    public string Status { get; set; } = string.Empty;
}
