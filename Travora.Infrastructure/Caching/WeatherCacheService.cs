using System.Text.Json;
using Travora.Application.DTOs.Airports;
using Travora.Application.Interfaces.External.Weather;
using StackExchange.Redis;

namespace Travora.Infrastructure.Caching;

public class WeatherCacheService : IWeatherCache
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WeatherCacheService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<WeatherDto?> GetAsync(string icaoCode)
    {
        var db = _redis.GetDatabase();
        var key = $"weather:airport:{icaoCode}";
        var cached = await db.StringGetAsync(key);

        if (cached.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<WeatherDto>(cached!, _jsonOptions);
    }

    public async Task SetAsync(string icaoCode, WeatherDto data, int ttlMinutes)
    {
        var db = _redis.GetDatabase();
        var key = $"weather:airport:{icaoCode}";
        var json = JsonSerializer.Serialize(data, _jsonOptions);

        await db.StringSetAsync(key, json, TimeSpan.FromMinutes(ttlMinutes));
    }
}
