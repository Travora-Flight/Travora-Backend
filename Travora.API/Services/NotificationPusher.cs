using Microsoft.AspNetCore.SignalR;
using Travora.API.Hubs;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Services;

public class NotificationPusher : INotificationPusher
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public NotificationPusher(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PushToEmployeeAsync(int employeeId, string title, string message, string type, int? orderId)
    {
        var payload = new
        {
            title,
            message,
            type,
            orderId,
            sentAt = DateTime.UtcNow
        };

        await _hubContext.Clients.Group($"employee_{employeeId}")
            .SendAsync("ReceiveNotification", payload);
    }

    public async Task PushToCustomerAsync(int customerId, string title, string message, string type, int? orderId)
    {
        var payload = new
        {
            title,
            message,
            type,
            orderId,
            sentAt = DateTime.UtcNow
        };

        await _hubContext.Clients.Group($"customer_{customerId}")
            .SendAsync("ReceiveNotification", payload);
    }
}
