using Travora.Application.DTOs.Airports;

namespace Travora.Application.Interfaces.Services;

public interface IAirportDetailsService
{
    Task<AirportDetailsResponse> GetAirportDetailsAsync(string icaoCode);
}
