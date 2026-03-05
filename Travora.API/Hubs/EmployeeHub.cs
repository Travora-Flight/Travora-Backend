using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Travora.API.Hubs;

[Authorize(Roles = "employee")]
public class EmployeeHub : Hub
{
    public async Task JoinEmployeeGroup()
    {
        var employeeId = Context.User?.FindFirstValue("employeeId");
        if (employeeId != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"employee:{employeeId}");
        }
    }

    public async Task LeaveEmployeeGroup()
    {
        var employeeId = Context.User?.FindFirstValue("employeeId");
        if (employeeId != null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"employee:{employeeId}");
        }
    }
}
