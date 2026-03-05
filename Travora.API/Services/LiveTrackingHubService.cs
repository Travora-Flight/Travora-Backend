using Microsoft.AspNetCore.SignalR;
using Travora.API.Hubs;
using Travora.Application.Interfaces.Hubs;

namespace Travora.API.Services;

public class LiveTrackingHubService : ILiveTrackingHubService
{
    private readonly IHubContext<LiveTrackingHub> _hubContext;

    public LiveTrackingHubService(IHubContext<LiveTrackingHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendLocationUpdate(object locationData)
    {
        await _hubContext.Clients.Group("admins_live_tracking")
            .SendAsync("EmployeeLocationUpdated", locationData);
    }
}
