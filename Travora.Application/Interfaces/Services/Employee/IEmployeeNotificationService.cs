using Travora.Application.DTOs.Employee.Notifications;

namespace Travora.Application.Interfaces.Services.Employee;

public interface IEmployeeNotificationService
{
    Task<EmployeeNotificationsResponse> GetNotificationsAsync(int employeeId, int page, int pageSize);
    Task MarkAsReadAsync(int employeeId, int notificationId);
    Task MarkAllAsReadAsync(int employeeId);
}
