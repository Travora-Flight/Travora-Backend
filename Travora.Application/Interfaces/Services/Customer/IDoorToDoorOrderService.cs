using Travora.Application.DTOs.Orders.DoorToDoor;

namespace Travora.Application.Interfaces.Services.Customer;

public interface IDoorToDoorOrderService
{
    Task<ValidateFlightResponse> ValidateFlightAsync(int customerId, ValidateFlightRequest request, CancellationToken cancellationToken = default);
    Task<ValidateCompanionResponse> ValidateCompanionAsync(int customerId, ValidateCompanionRequest request, CancellationToken cancellationToken = default);
    Task<ValidateBaggageResponse> ValidateBaggageAsync(int customerId, CancellationToken cancellationToken = default);
    Task<ResolveLocationResponse> ResolveLocationAsync(int customerId, ResolveLocationRequest request, CancellationToken cancellationToken = default);
    Task<ResolveLocationResponse> UpdateLocationAsync(int customerId, UpdateLocationRequest request, CancellationToken cancellationToken = default);
    Task<AvailableSlotsResponse> GetAvailableSlotsAsync(int customerId, DateTime date, CancellationToken cancellationToken = default);
    Task<AvailableSlotsResponse> GetAvailableDeliverySlotsAsync(int customerId, DateTime date, CancellationToken cancellationToken = default);
    Task<SetCustomsTypeResponse> SetCustomsTypeAsync(int customerId, SetCustomsTypeRequest request, CancellationToken cancellationToken = default);
    Task<CustomsLookupResponse> LookupCustomsProductAsync(string productName, CancellationToken cancellationToken = default);
    Task<AddCustomsItemResponse> AddCustomsItemAsync(int customerId, AddCustomsItemRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceResponse> GetInvoiceAsync(int customerId, CancellationToken cancellationToken = default);
    Task<ConfirmOrderResponse> ConfirmOrderAsync(int customerId, CancellationToken cancellationToken = default);
    Task AssignEmployeesAfterPaymentAsync(int orderId, CancellationToken cancellationToken = default);
}
