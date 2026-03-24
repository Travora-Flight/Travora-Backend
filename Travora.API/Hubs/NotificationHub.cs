using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Travora.API.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var employeeId = Context.User?.FindFirstValue("employeeId");
        var customerId = Context.User?.FindFirstValue("customerId");

        if (employeeId != null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"employee_{employeeId}");
        else if (customerId != null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"customer_{customerId}");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var employeeId = Context.User?.FindFirstValue("employeeId");
        var customerId = Context.User?.FindFirstValue("customerId");

        if (employeeId != null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"employee_{employeeId}");
        else if (customerId != null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"customer_{customerId}");

        await base.OnDisconnectedAsync(exception);
    }
}
