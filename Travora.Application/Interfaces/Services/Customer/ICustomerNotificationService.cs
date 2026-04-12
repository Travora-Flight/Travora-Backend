using Travora.Application.DTOs.Customer.Notifications;

namespace Travora.Application.Interfaces.Services.Customer;

public interface ICustomerNotificationService
{
    Task<CustomerNotificationsResponse> GetNotificationsAsync(int customerId, int page, int pageSize);
    Task MarkAsReadAsync(int customerId, int notificationId);
    Task MarkAllAsReadAsync(int customerId);
}
