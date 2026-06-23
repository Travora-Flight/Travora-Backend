namespace Travora.Application.Interfaces.Services;

public interface IFlightDelayPredictionJob
{
    Task PredictUpcomingFlightDelaysAsync();
}
