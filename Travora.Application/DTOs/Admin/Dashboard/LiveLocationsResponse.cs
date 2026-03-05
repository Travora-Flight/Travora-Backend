namespace Travora.Application.DTOs.Admin.Dashboard;

public class LiveLocationsResponse
{
    public int ActiveCount { get; set; }
    public List<LiveDriverItem> Drivers { get; set; } = new();
}

public class LiveDriverItem
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string Status { get; set; } = "available";
    public string? CurrentTask { get; set; }
    public decimal? SpeedKmh { get; set; }
    public bool IsMoving { get; set; }
    public string LastUpdated { get; set; } = "offline";
}
