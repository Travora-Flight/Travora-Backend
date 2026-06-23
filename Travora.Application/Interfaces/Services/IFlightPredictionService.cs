using Travora.Domain.Entities;
using Travora.Application.DTOs.Flights;
using System.Threading.Tasks;

namespace Travora.Application.Interfaces.Services;

public interface IFlightPredictionService
{
    Task<DelayPredictionResponseDto?> PredictAndNotifyFlightDelayAsync(Flight flight);
}
