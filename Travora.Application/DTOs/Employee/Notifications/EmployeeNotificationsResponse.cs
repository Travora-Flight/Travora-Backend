namespace Travora.Application.DTOs.Employee.Notifications;

public class EmployeeNotificationsResponse
{
    public int UnreadCount { get; set; }
    public List<EmployeeNotificationItemDto> Notifications { get; set; } = new();
}
