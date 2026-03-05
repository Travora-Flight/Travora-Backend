namespace Travora.Application.Interfaces.Hubs;

public interface ILiveTrackingHubService
{
    Task SendLocationUpdate(object locationData);
}
