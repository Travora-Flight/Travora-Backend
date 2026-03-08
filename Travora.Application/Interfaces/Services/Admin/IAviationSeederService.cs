using Travora.Application.DTOs.Admin.Seed;

namespace Travora.Application.Interfaces.Services.Admin;

public interface IAviationSeederService
{
    Task<SeedResult> SeedCountriesAsync();
    Task<SeedResult> SeedCitiesAsync();
    Task<SeedResult> SeedAirportsAsync();
    Task<SeedResult> SeedAirlinesAsync();
    Task<SeedResult> SeedAircraftAsync();
}
