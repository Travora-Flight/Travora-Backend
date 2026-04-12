namespace Travora.Application.DTOs.Customer.Notifications;

public class CustomerNotificationItemDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public int? BaggageId { get; set; }
    public bool IsRead { get; set; }
    public string SentAt { get; set; } = string.Empty;
}
