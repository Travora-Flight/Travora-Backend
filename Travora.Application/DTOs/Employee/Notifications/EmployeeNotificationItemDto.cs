namespace Travora.Application.DTOs.Employee.Notifications;

public class EmployeeNotificationItemDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public bool IsRead { get; set; }
    public string SentAt { get; set; } = string.Empty;
}
