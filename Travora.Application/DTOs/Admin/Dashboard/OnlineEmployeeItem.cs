namespace Travora.Application.DTOs.Admin.Dashboard;

public class OnlineEmployeeItem
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
}
