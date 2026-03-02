namespace Travora.Application.DTOs.Admin.LiveTracker;

public class LiveEmployeeResponse
{
    public int Available { get; set; }
    public int OnService { get; set; }
    public List<LiveEmployeeItem> Employees { get; set; } = new();
}
