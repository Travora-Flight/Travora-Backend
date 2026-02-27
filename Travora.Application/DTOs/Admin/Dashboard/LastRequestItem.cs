namespace Travora.Application.DTOs.Admin.Dashboard;

public class LastRequestItem
{
    public int OrderId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AssignedEmployee { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
}
