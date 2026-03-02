namespace Travora.Application.DTOs.Admin.LiveTracker;

public class LiveEmployeeItem
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string JobRole { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CurrentTask { get; set; }
    public string Location { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public bool IsOnline { get; set; }
    public string LastUpdated { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
}
