namespace Travora.Application.DTOs.Admin.Dashboard;

public class OnlineEmployeesResponse
{
    public int OnlineCount { get; set; }
    public List<OnlineEmployeeDetail> Employees { get; set; } = new();
}

public class OnlineEmployeeDetail
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string Status { get; set; } = "available";
    public string? CurrentTask { get; set; }
    public string LastUpdated { get; set; } = "offline";
}
