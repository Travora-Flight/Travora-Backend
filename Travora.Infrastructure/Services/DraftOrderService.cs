using System.Text.Json;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.DTOs.Orders.CarService;
using Travora.Application.DTOs.Orders.BagTracking;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.Services;

namespace Travora.Infrastructure.Services;

public class DraftOrderService : IDraftOrderService
{
    private readonly IUpstashRedisService _redis;
    private const string Prefix = "draft-order:";
    private const string CarServicePrefix = "car-service-draft:";
    private const string BagTrackingPrefix = "draft-bag-tracking:";

    public DraftOrderService(IUpstashRedisService redis)
    {
        _redis = redis;
    }

    // ===== Door To Door =====

    public async Task<DraftOrder?> GetDraftOrderAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var json = await _redis.GetAsync(Prefix + customerId);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<DraftOrder>(json);
    }

    public async Task SaveDraftOrderAsync(DraftOrder draftOrder, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(draftOrder);
        await _redis.SetAsync(Prefix + draftOrder.CustomerId, json, expiry ?? TimeSpan.FromMinutes(30));
    }

    public async Task RemoveDraftOrderAsync(string customerId, CancellationToken cancellationToken = default)
    {
        await _redis.DeleteAsync(Prefix + customerId);
    }

    // ===== Car Service =====

    public async Task<CarServiceDraftOrder?> GetCarServiceDraftAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var json = await _redis.GetAsync(CarServicePrefix + customerId);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<CarServiceDraftOrder>(json);
    }

    public async Task SaveCarServiceDraftAsync(CarServiceDraftOrder draftOrder, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(draftOrder);
        await _redis.SetAsync(CarServicePrefix + draftOrder.CustomerId, json, expiry ?? TimeSpan.FromMinutes(30));
    }

    public async Task RemoveCarServiceDraftAsync(string customerId, CancellationToken cancellationToken = default)
    {
        await _redis.DeleteAsync(CarServicePrefix + customerId);
    }

    // ===== Bag Tracking =====

    public async Task<BagTrackingDraftOrder?> GetBagTrackingDraftAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var json = await _redis.GetAsync(BagTrackingPrefix + customerId);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<BagTrackingDraftOrder>(json);
    }

    public async Task SaveBagTrackingDraftAsync(BagTrackingDraftOrder draftOrder, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(draftOrder);
        await _redis.SetAsync(BagTrackingPrefix + draftOrder.CustomerId, json, expiry ?? TimeSpan.FromMinutes(30));
    }

    public async Task RemoveBagTrackingDraftAsync(string customerId, CancellationToken cancellationToken = default)
    {
        await _redis.DeleteAsync(BagTrackingPrefix + customerId);
    }
}
