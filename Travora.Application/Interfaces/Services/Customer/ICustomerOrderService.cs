using Travora.Application.DTOs.Orders;
using Travora.Application.DTOs.Orders.DoorToDoor;
using Travora.Domain.Enums;

namespace Travora.Application.Interfaces.Services.Customer;

public interface ICustomerOrderService
{
    Task<IEnumerable<OrderListDto>> GetCustomerOrdersAsync(int customerId, OrderStatus? status = null, PackageFilter? package = null, CancellationToken cancellationToken = default);
    Task<OrderDetailsResponse> GetOrderDetailsAsync(int customerId, int orderId, CancellationToken cancellationToken = default);
    Task<CancelOrderResponse> CancelOrderAsync(int customerId, int orderId, string reason, CancellationToken cancellationToken = default);
    Task<AvailableDatesResponse> GetAvailableDatesForRescheduleAsync(int customerId, int orderId, RescheduleType type, CancellationToken cancellationToken = default);
    Task<AvailableSlotsResponse> GetAvailableSlotsForRescheduleAsync(int customerId, int orderId, RescheduleType type, DateTime date, CancellationToken cancellationToken = default);
    Task<RescheduleResponse> RescheduleOrderAsync(int customerId, int orderId, RescheduleRequest request, CancellationToken cancellationToken = default);
    Task<BoardingPassResponse> GetBoardingPassAsync(int customerId, int orderId, CancellationToken cancellationToken = default);
    Task<(byte[] PdfBytes, string FileName)> DownloadBoardingPassAsync(int customerId, int orderId, CancellationToken cancellationToken = default);
    Task GenerateBoardingPassesAsync(int orderId, CancellationToken cancellationToken = default);
}
