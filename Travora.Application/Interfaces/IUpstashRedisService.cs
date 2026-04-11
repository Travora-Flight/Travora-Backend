namespace Travora.Application.Interfaces;

public interface IUpstashRedisService
{
    Task SetAsync(string key, string value, TimeSpan? expiry = null);
    Task<string?> GetAsync(string key);
    Task DeleteAsync(string key);
    Task<bool> KeyExistsAsync(string key);
    Task<List<string>> KeysAsync(string pattern);
}
