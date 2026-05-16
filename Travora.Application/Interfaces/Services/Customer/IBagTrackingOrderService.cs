using Microsoft.AspNetCore.Http;
using Travora.Application.DTOs.Orders.BagTracking;
using Travora.Application.DTOs.Orders.DoorToDoor;

namespace Travora.Application.Interfaces.Services.Customer;

public interface IBagTrackingOrderService
{
    Task<ValidateFlightResponse> ValidateFlightAsync(int customerId, BagTrackingValidateFlightRequest request, CancellationToken cancellationToken = default);
    Task<ValidateCompanionResponse> ValidateCompanionAsync(int customerId, ValidateCompanionRequest request, CancellationToken cancellationToken = default);
    Task<BagTrackingValidateBaggageResponse> ValidateBaggageAsync(int customerId, CancellationToken cancellationToken = default);
    Task<ScanBagResponse> ScanBagAsync(int customerId, ScanBagRequest request, CancellationToken cancellationToken = default);
    Task<UploadBagPhotosResponse> UploadBagPhotosAsync(int customerId, string tagNumber, List<IFormFile> photos, CancellationToken cancellationToken = default);
    Task<InvoiceResponse> GetInvoiceAsync(int customerId, CancellationToken cancellationToken = default);
    Task<ConfirmOrderResponse> ConfirmOrderAsync(int customerId, CancellationToken cancellationToken = default);
}
