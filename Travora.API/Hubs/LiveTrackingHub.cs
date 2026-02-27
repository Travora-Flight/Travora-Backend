using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Travora.API.Hubs;

[Authorize(Roles = "admin")]
public class LiveTrackingHub : Hub
{
    public async Task JoinAdminTracking()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "admins_live_tracking");
    }

    public async Task LeaveAdminTracking()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "admins_live_tracking");
    }
}
