using Travora.Application.DTOs.Flights.Tracker;

namespace Travora.Application.Interfaces.Services;

public interface IAdsbExchangeService
{
    Task<List<AdsbAircraftDto>> GetAircraftInRadiusAsync(double lat, double lon, int radiusNm);
    Task<AdsbAircraftDto?> GetAircraftByCallsignAsync(string callsign);
    Task<AdsbAircraftDto?> GetAircraftByIcaoAsync(string icaoHex);
}
