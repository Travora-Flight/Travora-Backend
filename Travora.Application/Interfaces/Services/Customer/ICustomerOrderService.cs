using Travora.Application.DTOs.Orders;
using Travora.Application.DTOs.Orders.DoorToDoor;

namespace Travora.Application.Interfaces.Services.Customer;

public interface ICustomerOrderService
{
    Task<OrderDetailsResponse> GetOrderDetailsAsync(int customerId, int orderId, CancellationToken cancellationToken = default);
    Task<CancelOrderResponse> CancelOrderAsync(int customerId, int orderId, string reason, CancellationToken cancellationToken = default);
    Task<AvailableSlotsResponse> GetAvailableSlotsForRescheduleAsync(int customerId, int orderId, string type, DateTime date, CancellationToken cancellationToken = default);
    Task<RescheduleResponse> RescheduleOrderAsync(int customerId, int orderId, RescheduleRequest request, CancellationToken cancellationToken = default);
    Task<BoardingPassResponse> GetBoardingPassAsync(int customerId, int orderId, CancellationToken cancellationToken = default);
    Task<(byte[] PdfBytes, string FileName)> DownloadBoardingPassAsync(int customerId, int orderId, CancellationToken cancellationToken = default);
    Task GenerateBoardingPassesAsync(int orderId, CancellationToken cancellationToken = default);
}
