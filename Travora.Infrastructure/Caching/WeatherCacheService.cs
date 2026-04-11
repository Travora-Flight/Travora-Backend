using System.Text.Json;
using Travora.Application.DTOs.Airports;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.External.Weather;

namespace Travora.Infrastructure.Caching;

public class WeatherCacheService : IWeatherCache
{
    private readonly IUpstashRedisService _redis;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WeatherCacheService(IUpstashRedisService redis)
    {
        _redis = redis;
    }

    public async Task<WeatherDto?> GetAsync(string icaoCode)
    {
        var key = $"weather:airport:{icaoCode}";
        var cached = await _redis.GetAsync(key);

        if (string.IsNullOrEmpty(cached))
            return null;

        return JsonSerializer.Deserialize<WeatherDto>(cached, _jsonOptions);
    }

    public async Task SetAsync(string icaoCode, WeatherDto data, int ttlMinutes)
    {
        var key = $"weather:airport:{icaoCode}";
        var json = JsonSerializer.Serialize(data, _jsonOptions);

        await _redis.SetAsync(key, json, TimeSpan.FromMinutes(ttlMinutes));
    }
}
