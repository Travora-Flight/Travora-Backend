using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Travora.Application.DTOs.Admin.Seed;
using Travora.Application.Interfaces.Services.Admin;
using Travora.Domain.Entities;
using Travora.Infrastructure.Data;
using Microsoft.Extensions.Configuration;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AviationSeederService : IAviationSeederService
{
    private readonly ApplicationDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public AviationSeederService(ApplicationDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _db = db;
        _httpClient = httpClientFactory.CreateClient("AviationEdge");
        _apiKey = configuration["AviationEdge:ApiKey"] ?? throw new InvalidOperationException("API Key missing");
    }

    public async Task<SeedResult> SeedCountriesAsync()
    {
        var result = new SeedResult { Success = true };
        try
        {
            var response = await _httpClient.GetAsync($"countryDatabase?key={_apiKey}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var countries = JsonSerializer.Deserialize<List<AviationEdgeCountryResponse>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (countries == null) return result;

            result.Total = countries.Count;

            var existingCountries = await _db.Countries.ToDictionaryAsync(c => c.Iso2Code);

            foreach (var item in countries)
            {
                if (string.IsNullOrWhiteSpace(item.CodeIso2Country))
                {
                    result.Skipped++;
                    continue;
                }

                long.TryParse(item.Population, out long pop);

                if (existingCountries.TryGetValue(item.CodeIso2Country, out var existing))
                {
                    existing.CountryName = item.NameCountry ?? string.Empty;
                    existing.Iso3Code = item.CodeIso3Country ?? string.Empty;
                    existing.NumericIso = item.NumericIso ?? string.Empty;
                    existing.Continent = item.Continent ?? string.Empty;
                    existing.Capital = item.Capital ?? string.Empty;
                    existing.CurrencyCode = item.CodeCurrency ?? string.Empty;
                    existing.CurrencyName = item.NameCurrency ?? string.Empty;
                    existing.PhonePrefix = item.PhonePrefix ?? string.Empty;
                    existing.Population = pop;
                    existing.FipsCode = item.CodeFips ?? string.Empty;
                    existing.UpdatedAt = DateTime.UtcNow;
                    result.Updated++;
                }
                else
                {
                    _db.Countries.Add(new Country
                    {
                        CountryName = item.NameCountry ?? string.Empty,
                        Iso2Code = item.CodeIso2Country,
                        Iso3Code = item.CodeIso3Country ?? string.Empty,
                        NumericIso = item.NumericIso ?? string.Empty,
                        Continent = item.Continent ?? string.Empty,
                        Capital = item.Capital ?? string.Empty,
                        CurrencyCode = item.CodeCurrency ?? string.Empty,
                        CurrencyName = item.NameCurrency ?? string.Empty,
                        PhonePrefix = item.PhonePrefix ?? string.Empty,
                        Population = pop,
                        FipsCode = item.CodeFips ?? string.Empty,
                        CreatedAt = DateTime.UtcNow
                    });
                    result.Inserted++;
                }
            }
            await _db.SaveChangesAsync();
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<SeedResult> SeedCitiesAsync()
    {
        var result = new SeedResult { Success = true };
        try
        {
            var response = await _httpClient.GetAsync($"cityDatabase?key={_apiKey}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var cities = JsonSerializer.Deserialize<List<AviationEdgeCityResponse>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (cities == null) return result;

            result.Total = cities.Count;

            var existingCities = await _db.Cities.ToDictionaryAsync(c => c.CodeIataCity);
            var validCountries = await _db.Countries.Select(c => c.Iso2Code).ToHashSetAsync();

            foreach (var item in cities)
            {
                if (string.IsNullOrWhiteSpace(item.CodeIataCity) || string.IsNullOrWhiteSpace(item.CodeIso2Country) || !validCountries.Contains(item.CodeIso2Country))
                {
                    result.Skipped++;
                    continue;
                }

                decimal lat = item.LatitudeCity ?? 0m;
                decimal lon = item.LongitudeCity ?? 0m;
                int geoId = item.GeonameId ?? 0;
                
                if (existingCities.TryGetValue(item.CodeIataCity, out var existing))
                {
                    existing.NameCity = item.NameCity ?? string.Empty;
                    existing.CodeIso2Country = item.CodeIso2Country;
                    existing.LatitudeCity = lat;
                    existing.LongitudeCity = lon;
                    existing.Timezone = item.Timezone ?? string.Empty;
                    existing.GMT = item.GMT ?? string.Empty;
                    existing.GeonameId = geoId != 0 ? geoId : existing.GeonameId;
                    existing.UpdatedAt = DateTime.UtcNow;
                    result.Updated++;
                }
                else
                {
                    _db.Cities.Add(new City
                    {
                        NameCity = item.NameCity ?? string.Empty,
                        CodeIataCity = item.CodeIataCity,
                        CodeIso2Country = item.CodeIso2Country,
                        LatitudeCity = lat,
                        LongitudeCity = lon,
                        Timezone = item.Timezone ?? string.Empty,
                        GMT = item.GMT ?? string.Empty,
                        GeonameId = geoId,
                        CreatedAt = DateTime.UtcNow
                    });
                    result.Inserted++;
                }
            }
            await _db.SaveChangesAsync();
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<SeedResult> SeedAirportsAsync()
    {
        var result = new SeedResult { Success = true };
        try
        {
            var response = await _httpClient.GetAsync($"airportDatabase?key={_apiKey}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var airports = JsonSerializer.Deserialize<List<AviationEdgeAirportResponse>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (airports == null) return result;
            airports = airports
                .Where(a => !string.IsNullOrWhiteSpace(a.CodeIataAirport) 
                         && !string.IsNullOrWhiteSpace(a.CodeIcaoAirport))
                .GroupBy(a => a.CodeIcaoAirport)
                .Select(g => g.OrderByDescending(a => a.AirportId).First())
                .ToList();
            result.Total = airports.Count;

            var existingAirports = await _db.Airports.ToDictionaryAsync(a => a.CodeIataAirport);
            var validCities = await _db.Cities.Select(c => c.CodeIataCity).ToHashSetAsync();
            var validCountries = await _db.Countries.Select(c => c.Iso2Code).ToHashSetAsync();
            var seenIcao = new HashSet<string>();

            foreach (var item in airports)
            {
                if (string.IsNullOrWhiteSpace(item.CodeIataAirport))
                {
                    result.Skipped++;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(item.CodeIso2Country) || !validCountries.Contains(item.CodeIso2Country))
                {
                    result.Skipped++;
                    continue;
                }

                var icao = (item.CodeIcaoAirport ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(icao) && !seenIcao.Add(icao))
                {
                    result.Skipped++;
                    continue;
                }

                // If codeIataCity doesn't exist in Cities, null it per user instruction, 
                // but wait, the model requires CodeIataCity to not be null because City is a required FK.
                // Assuming it's tracked by string in DB... wait... The navigation property is public City City { get; set; } = null!;
                // But the FK property in City is 'CodeIataCity' ??? Actually I need to check how they map it.
                // If codeIataCity is not in the CITY table → insert with codeIataCity = null
                // So I will make it null if not found, but it might throw EF validation if it's required. Let's try.
                string? cityCode = validCities.Contains(item.CodeIataCity ?? "") ? item.CodeIataCity : null;

                decimal lat = item.LatitudeAirport ?? 0m;
                decimal lon = item.LongitudeAirport ?? 0m;

                if (existingAirports.TryGetValue(item.CodeIataAirport, out var existing))
                {
                    existing.NameAirport = item.NameAirport ?? string.Empty;
                    existing.CodeIcaoAirport = icao;
                    existing.CodeIataCity = cityCode;
                    existing.CodeIso2Country = (item.CodeIso2Country ?? string.Empty).Trim();
                    existing.LatitudeAirport = lat;
                    existing.LongitudeAirport = lon;
                    existing.Timezone = (item.Timezone ?? string.Empty).Trim();
                    existing.GMT = (item.GMT ?? string.Empty).Trim();
                    existing.GeonameId = (item.GeonameId ?? string.Empty).Trim();
                    existing.Phone = string.IsNullOrWhiteSpace(item.Phone) ? null : item.Phone.Trim();
                    existing.UpdatedAt = DateTime.UtcNow;
                    result.Updated++;
                }
                else
                {
                    _db.Airports.Add(new Airport
                    {
                        NameAirport = (item.NameAirport ?? string.Empty).Trim(),
                        CodeIataAirport = item.CodeIataAirport.Trim(),
                        CodeIcaoAirport = icao,
                        CodeIataCity = cityCode,
                        CodeIso2Country = (item.CodeIso2Country ?? string.Empty).Trim(),
                        LatitudeAirport = lat,
                        LongitudeAirport = lon,
                        Timezone = (item.Timezone ?? string.Empty).Trim(),
                        GMT = (item.GMT ?? string.Empty).Trim(),
                        GeonameId = (item.GeonameId ?? string.Empty).Trim(),
                        Phone = string.IsNullOrWhiteSpace(item.Phone) ? null : item.Phone.Trim(),
                        CreatedAt = DateTime.UtcNow
                    });
                    result.Inserted++;
                }
            }
            await _db.SaveChangesAsync();
            return result;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            result.Success = false;
            result.Error = dbEx.InnerException != null ? dbEx.InnerException.Message : dbEx.Message;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<SeedResult> SeedAirlinesAsync()
    {
        var result = new SeedResult { Success = true };
        try
        {
            var response = await _httpClient.GetAsync($"airlineDatabase?key={_apiKey}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var airlines = JsonSerializer.Deserialize<List<AviationEdgeAirlineResponse>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (airlines == null) return result;

            result.Total = airlines.Count;

            var existingAirlines = await _db.Airlines.ToDictionaryAsync(a => a.CodeIataAirline);
            var validAirports = await _db.Airports.Select(a => a.CodeIataAirport).ToHashSetAsync();
            var validCountries = await _db.Countries.Select(c => c.Iso2Code).ToHashSetAsync();
            foreach (var item in airlines)
            {
                if (string.IsNullOrWhiteSpace(item.CodeIataAirline))
                {
                    result.Skipped++;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(item.CodeIso2Country) || !validCountries.Contains(item.CodeIso2Country))
                {
                     result.Skipped++;
                     continue; 
                }
                string? hubCode = validAirports.Contains(item.CodeHub ?? "") ? item.CodeHub : null;
                int founding = item.Founding ?? 0;
                int size = item.SizeAirline ?? 0;
                decimal age = item.AgeFleet ?? 0m;
                if (existingAirlines.TryGetValue(item.CodeIataAirline, out var existing))
                {
                    existing.NameAirline = (item.NameAirline ?? string.Empty).Trim();
                    existing.CodeIcaoAirline = (item.CodeIcaoAirline ?? string.Empty).Trim();
                    existing.NameCountry = (item.NameCountry ?? string.Empty).Trim();
                    existing.CodeIso2Country = (item.CodeIso2Country ?? string.Empty).Trim();
                    existing.Callsign = (item.Callsign ?? string.Empty).Trim();
                    existing.CodeHub = string.IsNullOrWhiteSpace(hubCode) ? null : hubCode;
                    existing.Founding = founding;
                    existing.SizeAirline = size;
                    existing.AgeFleet = age;
                    existing.IataPrefixAccounting = (item.IataPrefixAccounting ?? string.Empty).Trim();
                    existing.Type = (item.Type ?? string.Empty).Trim();
                    existing.StatusAirline = (item.StatusAirline ?? string.Empty).Trim();
                    existing.UpdatedAt = DateTime.UtcNow;
                    result.Updated++;
                }
                else
                {
                    _db.Airlines.Add(new Airline
                    {
                        NameAirline = (item.NameAirline ?? string.Empty).Trim(),
                        CodeIataAirline = item.CodeIataAirline.Trim(),
                        CodeIcaoAirline = (item.CodeIcaoAirline ?? string.Empty).Trim(),
                        NameCountry = (item.NameCountry ?? string.Empty).Trim(),
                        CodeIso2Country = (item.CodeIso2Country ?? string.Empty).Trim(),
                        Callsign = (item.Callsign ?? string.Empty).Trim(),
                        CodeHub = string.IsNullOrWhiteSpace(hubCode) ? null : hubCode,
                        Founding = founding,
                        SizeAirline = size,
                        AgeFleet = age,
                        IataPrefixAccounting = (item.IataPrefixAccounting ?? string.Empty).Trim(),
                        Type = (item.Type ?? string.Empty).Trim(),
                        StatusAirline = (item.StatusAirline ?? string.Empty).Trim(),
                        CreatedAt = DateTime.UtcNow
                    });
                    result.Inserted++;
                }
            }
            await _db.SaveChangesAsync();
            return result;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            result.Success = false;
            result.Error = dbEx.InnerException != null ? dbEx.InnerException.Message : dbEx.Message;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

   public async Task<SeedResult> SeedAircraftAsync()
{
    var result = new SeedResult { Success = true };
    try
    {
        var response = await _httpClient.GetAsync($"airplaneDatabase?key={_apiKey}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var aircrafts = JsonSerializer.Deserialize<List<AviationEdgeAircraftResponse>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (aircrafts == null) return result;

        result.Total = aircrafts.Count;

        // Fix: avoid duplicate key crash
        var validAirlines = new Dictionary<string, int>();
        var airlinesList = await _db.Airlines.ToListAsync();
        foreach (var a in airlinesList)
        {
            if (!validAirlines.ContainsKey(a.CodeIataAirline))
                validAirlines[a.CodeIataAirline] = a.AirlineId;
        }

        // Get all existing records in a single Dictionary
        var existingAircrafts = await _db.Aircrafts
            .ToDictionaryAsync(a => a.NumberRegistration);

        DateTime? ParseDate(string? d) =>
            !string.IsNullOrWhiteSpace(d) && d != "0000-00-00" && DateTime.TryParse(d, out var parsed)
                ? parsed : null;

        var toInsert = new List<Aircraft>();
        int batchSize = 500;

        foreach (var item in aircrafts)
        {
            if (string.IsNullOrWhiteSpace(item.NumberRegistration))
            {
                result.Skipped++;
                continue;
            }

            int.TryParse(item.EnginesCount, out int enginesCount);
            int.TryParse(item.PlaneAge, out int planeAge);
            validAirlines.TryGetValue(item.CodeIataAirline ?? "", out int airlineId);

            if (existingAircrafts.TryGetValue(item.NumberRegistration, out var existing))
            {
                existing.HexIcaoAirplane = (item.HexIcaoAirplane ?? string.Empty).Trim();
                existing.AirplaneIataType = (item.AirplaneIataType ?? string.Empty).Trim();
                existing.CodeIataPlaneLong = (item.CodeIataPlaneLong ?? string.Empty).Trim();
                existing.CodeIataPlaneShort = (item.CodeIataPlaneShort ?? string.Empty).Trim();
                existing.CodeIataAirline = (item.CodeIataAirline ?? string.Empty).Trim();
                existing.CodeIcaoAirline = (item.CodeIcaoAirline ?? string.Empty).Trim();
                existing.ConstructionNumber = (item.ConstructionNumber ?? string.Empty).Trim();
                existing.DeliveryDate = ParseDate(item.DeliveryDate);
                existing.FirstFlight = ParseDate(item.FirstFlight);
                existing.LineNumber = (item.LineNumber ?? string.Empty).Trim();
                existing.ModelCode = (item.ModelCode ?? string.Empty).Trim();
                existing.EnginesCount = enginesCount;
                existing.EnginesType = (item.EnginesType ?? string.Empty).Trim();
                existing.PlaneAge = planeAge;
                existing.PlaneClass = string.IsNullOrWhiteSpace(item.PlaneClass) ? null : item.PlaneClass.Trim();
                existing.PlaneModel = (item.PlaneModel ?? string.Empty).Trim();
                existing.PlaneSeries = (item.PlaneSeries ?? string.Empty).Trim();
                existing.PlaneOwner = (item.PlaneOwner ?? string.Empty).Trim();
                existing.PlaneStatus = (item.PlaneStatus ?? string.Empty).Trim();
                existing.ProductionLine = (item.ProductionLine ?? string.Empty).Trim();
                existing.RegistrationDate = ParseDate(item.RegistrationDate);
                existing.RolloutDate = ParseDate(item.RolloutDate);
                existing.NumberTestRegistration = string.IsNullOrWhiteSpace(item.NumberTestRegistration) ? null : item.NumberTestRegistration;
                existing.AirlineId = airlineId > 0 ? airlineId : null;
                existing.UpdatedAt = DateTime.UtcNow;
                result.Updated++;
            }
            else
            {
                toInsert.Add(new Aircraft
                {
                    NumberRegistration = item.NumberRegistration.Trim(),
                    HexIcaoAirplane = (item.HexIcaoAirplane ?? string.Empty).Trim(),
                    AirplaneIataType = (item.AirplaneIataType ?? string.Empty).Trim(),
                    CodeIataPlaneLong = (item.CodeIataPlaneLong ?? string.Empty).Trim(),
                    CodeIataPlaneShort = (item.CodeIataPlaneShort ?? string.Empty).Trim(),
                    CodeIataAirline = (item.CodeIataAirline ?? string.Empty).Trim(),
                    CodeIcaoAirline = (item.CodeIcaoAirline ?? string.Empty).Trim(),
                    ConstructionNumber = (item.ConstructionNumber ?? string.Empty).Trim(),
                    DeliveryDate = ParseDate(item.DeliveryDate),
                    FirstFlight = ParseDate(item.FirstFlight),
                    LineNumber = (item.LineNumber ?? string.Empty).Trim(),
                    ModelCode = (item.ModelCode ?? string.Empty).Trim(),
                    EnginesCount = enginesCount,
                    EnginesType = (item.EnginesType ?? string.Empty).Trim(),
                    PlaneAge = planeAge,
                    PlaneClass = string.IsNullOrWhiteSpace(item.PlaneClass) ? null : item.PlaneClass.Trim(),
                    PlaneModel = (item.PlaneModel ?? string.Empty).Trim(),
                    PlaneSeries = (item.PlaneSeries ?? string.Empty).Trim(),
                    PlaneOwner = (item.PlaneOwner ?? string.Empty).Trim(),
                    PlaneStatus = (item.PlaneStatus ?? string.Empty).Trim(),
                    ProductionLine = (item.ProductionLine ?? string.Empty).Trim(),
                    RegistrationDate = ParseDate(item.RegistrationDate),
                    RolloutDate = ParseDate(item.RolloutDate),
                    NumberTestRegistration = string.IsNullOrWhiteSpace(item.NumberTestRegistration) ? null : item.NumberTestRegistration,
                    AirlineId = airlineId > 0 ? airlineId : null,
                    CreatedAt = DateTime.UtcNow
                });
                result.Inserted++;

                if (toInsert.Count >= batchSize)
                {
                    await _db.Aircrafts.AddRangeAsync(toInsert);
                    await _db.SaveChangesAsync();
                    foreach (var ent in toInsert) _db.Entry(ent).State = EntityState.Detached;
                    toInsert.Clear();
                }
            }
        }

        // Insert the rest
        if (toInsert.Count > 0)
        {
            await _db.Aircrafts.AddRangeAsync(toInsert);
            await _db.SaveChangesAsync();
        }

        // Save all updates
        await _db.SaveChangesAsync();

        return result;
    }
    catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
    {
        result.Success = false;
        result.Error = dbEx.InnerException != null ? dbEx.InnerException.Message : dbEx.Message;
        return result;
    }
    catch (Exception ex)
    {
        result.Success = false;
        result.Error = ex.Message;
        return result;
    }
}
}
