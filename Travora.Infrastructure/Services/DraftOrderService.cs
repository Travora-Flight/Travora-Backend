using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.DTOs.Orders.CarService;
using Travora.Application.DTOs.Orders.BagTracking;
using Travora.Application.Interfaces.Services;

namespace Travora.Infrastructure.Services;

public class DraftOrderService : IDraftOrderService
{
    private readonly IDistributedCache _cache;
    private const string Prefix = "draft-order:";
    private const string CarServicePrefix = "car-service-draft:";
    private const string BagTrackingPrefix = "draft-bag-tracking:";

    public DraftOrderService(IDistributedCache cache)
    {
        _cache = cache;
    }

    // ===== Door To Door =====

    public async Task<DraftOrder?> GetDraftOrderAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(Prefix + customerId, cancellationToken);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<DraftOrder>(json);
    }

    public async Task SaveDraftOrderAsync(DraftOrder draftOrder, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromMinutes(30)
        };
        var json = JsonSerializer.Serialize(draftOrder);
        await _cache.SetStringAsync(Prefix + draftOrder.CustomerId, json, options, cancellationToken);
    }

    public async Task RemoveDraftOrderAsync(string customerId, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(Prefix + customerId, cancellationToken);
    }

    // ===== Car Service =====

    public async Task<CarServiceDraftOrder?> GetCarServiceDraftAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(CarServicePrefix + customerId, cancellationToken);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<CarServiceDraftOrder>(json);
    }

    public async Task SaveCarServiceDraftAsync(CarServiceDraftOrder draftOrder, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromMinutes(30)
        };
        var json = JsonSerializer.Serialize(draftOrder);
        await _cache.SetStringAsync(CarServicePrefix + draftOrder.CustomerId, json, options, cancellationToken);
    }

    public async Task RemoveCarServiceDraftAsync(string customerId, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(CarServicePrefix + customerId, cancellationToken);
    }

    // ===== Bag Tracking =====

    public async Task<BagTrackingDraftOrder?> GetBagTrackingDraftAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(BagTrackingPrefix + customerId, cancellationToken);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<BagTrackingDraftOrder>(json);
    }

    public async Task SaveBagTrackingDraftAsync(BagTrackingDraftOrder draftOrder, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromMinutes(30)
        };
        var json = JsonSerializer.Serialize(draftOrder);
        await _cache.SetStringAsync(BagTrackingPrefix + draftOrder.CustomerId, json, options, cancellationToken);
    }

    public async Task RemoveBagTrackingDraftAsync(string customerId, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(BagTrackingPrefix + customerId, cancellationToken);
    }
}
