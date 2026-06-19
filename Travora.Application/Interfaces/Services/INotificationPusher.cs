namespace Travora.Application.Interfaces.Services;

public interface INotificationPusher
{
    Task PushToEmployeeAsync(int employeeId, string title, string message, string type, int? orderId);
    Task PushToCustomerAsync(int customerId, string title, string message, string type, int? orderId);
    Task PushToGuestAsync(string guestId, string title, string message, string type, int? orderId);
}
