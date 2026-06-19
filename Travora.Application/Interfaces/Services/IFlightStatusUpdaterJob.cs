namespace Travora.Application.Interfaces.Services;

public interface IFlightStatusUpdaterJob
{
    Task UpdateFlightStatusesAsync();
}
