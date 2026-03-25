using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Travora.Application.DTOs.Airports;
using Travora.Application.Interfaces.External.Weather;
using Travora.Application.Interfaces.Services;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.Services;

public class AirportDetailsService : IAirportDetailsService
{
    private readonly ApplicationDbContext _db;
    private readonly IAviationWeatherService _weatherApi;
    private readonly IWeatherCache _weatherCache;
    private readonly int _cacheTtlMinutes;

    public AirportDetailsService(
        ApplicationDbContext db,
        IAviationWeatherService weatherApi,
        IWeatherCache weatherCache,
        IConfiguration configuration)
    {
        _db = db;
        _weatherApi = weatherApi;
        _weatherCache = weatherCache;
        _cacheTtlMinutes = configuration.GetValue<int>("AviationWeather:CacheTtlMinutes", 30);
    }

    public async Task<AirportDetailsResponse> GetAirportDetailsAsync(string icaoCode)
    {
        // 1) Find airport
        var airport = await _db.Airports
            .Include(a => a.City)
            .FirstOrDefaultAsync(a => a.CodeIcaoAirport == icaoCode);

        if (airport == null)
            throw new KeyNotFoundException("المطار غير موجود");

        // 2) Get weather
        var weather = await GetWeatherAsync(icaoCode, airport);

        // 3) Get flights
        var (flights, totalFlights) = await GetTodayFlightsAsync(airport);

        // 4) Build response
        return new AirportDetailsResponse
        {
            AirportName = airport.NameAirport,
            City = airport.City != null
                ? $"{airport.City.NameCity}, {airport.CodeIso2Country}"
                : airport.CodeIso2Country,
            IataCode = airport.CodeIataAirport,
            IcaoCode = airport.CodeIcaoAirport,
            Location = airport.City != null
                ? $"{airport.City.NameCity}, {airport.CodeIso2Country}"
                : airport.CodeIso2Country,
            TimeZone = FormatGmt(airport.GMT),
            Weather = weather,
            TotalFlights = totalFlights,
            Flights = flights
        };
    }

    private async Task<WeatherDto?> GetWeatherAsync(string icaoCode, Airport airport)
    {
        // Check Redis cache first
        var cached = await _weatherCache.GetAsync(icaoCode);
        if (cached != null)
            return cached;

        // Fetch from Aviation Weather API
        var weather = await _weatherApi.GetMetarAsync(icaoCode);
        if (weather == null)
            return null;

        // Save to DB
        await SaveWeatherSnapshotAsync(icaoCode, weather);

        // Cache in Redis
        await _weatherCache.SetAsync(icaoCode, weather, _cacheTtlMinutes);

        return weather;
    }

    private async Task SaveWeatherSnapshotAsync(string icaoCode, WeatherDto weather)
    {
        var snapshot = new WeatherSnapshot
        {
            IcaoId = icaoCode,
            SnapshotTimestamp = DateTime.UtcNow,
            Temperature = weather.Temperature,
            Dewpoint = weather.Dewpoint,
            WindDirection = weather.WindDirection,
            WindSpeed = weather.WindSpeed,
            Visibility = weather.Visibility,
            Altimeter = weather.Altimeter,
            MetarType = weather.MetarType,
            RawObservation = weather.RawObservation,
            Elevation = 0,
            CloudCover = weather.CloudCover,
            FlightCategory = ParseFlightCategory(weather.FlightCategory),
            ReportTime = weather.ReportTime,
            ReceiptTime = DateTime.UtcNow
        };

        _db.WeatherSnapshots.Add(snapshot);
        await _db.SaveChangesAsync();

        // Save cloud layers
        if (weather.CloudLayers.Any())
        {
            foreach (var layer in weather.CloudLayers)
            {
                _db.CloudLayers.Add(new CloudLayer
                {
                    WeatherSnapshotId = snapshot.WeatherSnapshotId,
                    CoverType = layer.Cover,
                    BaseAltitudeFeet = layer.Base
                });
            }
            await _db.SaveChangesAsync();
        }
    }

    private async Task<(List<AirportFlightDto> Flights, int Total)> GetTodayFlightsAsync(Airport airport)
    {
        var today = DateTime.UtcNow.Date;

        var departures = await _db.Flights
            .Where(f => f.DepartureAirportId == airport.AirportId
                && f.ScheduledDepartureTime.Date == today
                && f.FlightStatus != FlightStatus.Cancelled)
            .OrderBy(f => f.ScheduledDepartureTime)
            .Take(20)
            .Select(f => new AirportFlightDto
            {
                Destination = f.ArrivalAirport != null ? f.ArrivalAirport.NameAirport : f.ArrivalIataCode,
                FlightNumber = f.FlightIataNumber,
                ScheduledTime = f.ScheduledDepartureTime.ToString("HH:mm"),
                Gate = f.DepartureGate ?? "—",
                Type = "Departure",
                Status = MapFlightStatus(f.FlightStatus)
            })
            .ToListAsync();

        var arrivals = await _db.Flights
            .Where(f => f.ArrivalAirportId == airport.AirportId
                && f.ScheduledArrivalTime.Date == today
                && f.FlightStatus != FlightStatus.Cancelled)
            .OrderBy(f => f.ScheduledArrivalTime)
            .Take(20)
            .Select(f => new AirportFlightDto
            {
                Destination = f.DepartureAirport != null ? f.DepartureAirport.NameAirport : f.DepartureIataCode,
                FlightNumber = f.FlightIataNumber,
                ScheduledTime = f.ScheduledArrivalTime.ToString("HH:mm"),
                Gate = f.ArrivalGate ?? "—",
                Type = "Arrival",
                Status = MapFlightStatus(f.FlightStatus)
            })
            .ToListAsync();

        var combined = departures
            .Concat(arrivals)
            .OrderBy(f => f.ScheduledTime)
            .ToList();

        return (combined, combined.Count);
    }

    private static string FormatGmt(string gmt)
    {
        if (string.IsNullOrWhiteSpace(gmt))
            return "GMT";

        gmt = gmt.Trim();

        if (gmt.StartsWith("-"))
            return $"GMT{gmt}";

        return $"GMT+{gmt}";
    }

    private static FlightCategory ParseFlightCategory(string category)
    {
        return category?.ToUpper() switch
        {
            "VFR" => Domain.Enums.FlightCategory.VFR,
            "IFR" => Domain.Enums.FlightCategory.IFR,
            "MVFR" => Domain.Enums.FlightCategory.MVFR,
            "LIFR" => Domain.Enums.FlightCategory.LIFR,
            _ => Domain.Enums.FlightCategory.VFR
        };
    }

    private static string MapFlightStatus(FlightStatus status)
    {
        return status switch
        {
            FlightStatus.Scheduled => "On Time",
            FlightStatus.Delayed => "Delayed",
            FlightStatus.Boarding => "Boarding",
            FlightStatus.Departed => "Departed",
            FlightStatus.InAir => "In Air",
            FlightStatus.Landed => "Landed",
            FlightStatus.Cancelled => "Cancelled",
            _ => "Unknown"
        };
    }
}
