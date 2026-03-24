using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Application.DTOs.Orders.CarService;
using Travora.Application.DTOs.Orders.BagTracking;

namespace Travora.Application.Interfaces.Services;

public interface IDraftOrderService
{
    // Door To Door
    Task<DraftOrder?> GetDraftOrderAsync(string customerId, CancellationToken cancellationToken = default);
    Task SaveDraftOrderAsync(DraftOrder draftOrder, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task RemoveDraftOrderAsync(string customerId, CancellationToken cancellationToken = default);

    // Car Service
    Task<CarServiceDraftOrder?> GetCarServiceDraftAsync(string customerId, CancellationToken cancellationToken = default);
    Task SaveCarServiceDraftAsync(CarServiceDraftOrder draftOrder, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task RemoveCarServiceDraftAsync(string customerId, CancellationToken cancellationToken = default);

    // Bag Tracking
    Task<BagTrackingDraftOrder?> GetBagTrackingDraftAsync(string customerId, CancellationToken cancellationToken = default);
    Task SaveBagTrackingDraftAsync(BagTrackingDraftOrder draftOrder, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task RemoveBagTrackingDraftAsync(string customerId, CancellationToken cancellationToken = default);
}
