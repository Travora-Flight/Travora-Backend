using Travora.Application.DTOs.Orders.CarService;
using Travora.Application.DTOs.Orders.DoorToDoor;

namespace Travora.Application.Interfaces.Services.Customer;

public interface ICarServiceOrderService
{
    Task<CarServiceValidateFlightResponse> ValidateFlightAsync(int customerId, CarServiceValidateFlightRequest request, CancellationToken cancellationToken = default);
    Task<ValidateCompanionResponse> ValidateCompanionAsync(int customerId, ValidateCompanionRequest request, CancellationToken cancellationToken = default);
    Task<ValidateBaggageResponse> ValidateBaggageAsync(int customerId, CancellationToken cancellationToken = default);
    Task<ResolveLocationResponse> ResolveLocationAsync(int customerId, CarServiceResolveLocationRequest request, CancellationToken cancellationToken = default);
    Task<AvailableSlotsResponse> GetAvailableSlotsAsync(int customerId, DateTime date, CancellationToken cancellationToken = default);
    Task<MyBagsResponse> GetMyBagsAsync(int customerId, CancellationToken cancellationToken = default);
    Task SelectBagsAsync(int customerId, SelectBagsRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceResponse> GetInvoiceAsync(int customerId, CancellationToken cancellationToken = default);
    Task<ConfirmOrderResponse> ConfirmOrderAsync(int customerId, CancellationToken cancellationToken = default);
    Task AssignEmployeesAfterPaymentAsync(int orderId, CancellationToken cancellationToken = default);
}
