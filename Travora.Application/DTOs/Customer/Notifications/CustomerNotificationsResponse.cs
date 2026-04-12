namespace Travora.Application.DTOs.Customer.Notifications;

public class CustomerNotificationsResponse
{
    public int UnreadCount { get; set; }
    public List<CustomerNotificationItemDto> Notifications { get; set; } = new();
}
