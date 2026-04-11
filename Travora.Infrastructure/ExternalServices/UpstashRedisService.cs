using System.Text.Json;
using Travora.Application.Interfaces;

namespace Travora.Infrastructure.ExternalServices;

public class UpstashRedisService : IUpstashRedisService
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public UpstashRedisService(IHttpClientFactory httpClientFactory)
    {
        _client = httpClientFactory.CreateClient("UpstashRedis");
    }

    public async Task SetAsync(string key, string value, TimeSpan? expiry = null)
    {
        var encodedKey = Uri.EscapeDataString(key);
        var encodedValue = Uri.EscapeDataString(value);
        var url = expiry.HasValue
            ? $"/set/{encodedKey}/{encodedValue}?ex={(int)expiry.Value.TotalSeconds}"
            : $"/set/{encodedKey}/{encodedValue}";
        await _client.GetAsync(url);
    }

    public async Task<string?> GetAsync(string key)
    {
        var encodedKey = Uri.EscapeDataString(key);
        var response = await _client.GetAsync($"/get/{encodedKey}");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<UpstashResponse>(json, _jsonOptions);
        return result?.Result;
    }

    public async Task DeleteAsync(string key)
    {
        var encodedKey = Uri.EscapeDataString(key);
        await _client.GetAsync($"/del/{encodedKey}");
    }

    public async Task<bool> KeyExistsAsync(string key)
    {
        var encodedKey = Uri.EscapeDataString(key);
        var response = await _client.GetAsync($"/exists/{encodedKey}");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<UpstashIntResponse>(json, _jsonOptions);
        return result?.Result == 1;
    }

    public async Task<List<string>> KeysAsync(string pattern)
    {
        var encodedPattern = Uri.EscapeDataString(pattern);
        var response = await _client.GetAsync($"/keys/{encodedPattern}");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<UpstashListResponse>(json, _jsonOptions);
        return result?.Result ?? new List<string>();
    }

    private class UpstashResponse
    {
        public string? Result { get; set; }
    }

    private class UpstashIntResponse
    {
        public int Result { get; set; }
    }

    private class UpstashListResponse
    {
        public List<string>? Result { get; set; }
    }
}
