namespace Travora.Application.DTOs.Admin.Dashboard;

public class RecentOrdersResponse
{
    public List<RecentOrderItem> Orders { get; set; } = new();
}

public class RecentOrderItem
{
    public int OrderId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
    public string Time { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
}
