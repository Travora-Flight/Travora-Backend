using Microsoft.AspNetCore.Http;
using Travora.Application.DTOs.Customer.Profile;

namespace Travora.Application.Interfaces.Services.Customer;

public interface ICustomerProfileService
{
    Task<CustomerProfileResponse> GetProfileAsync(int customerId);
    Task<CustomerAccountResponse> GetAccountInfoAsync(int customerId);
    Task<(bool Success, string Message)> UpdateAccountAsync(int customerId, UpdateAccountRequest request, IFormFile? profileImage);
    Task<UploadPhotoResponse> UploadPhotoAsync(int customerId, IFormFile photo);
    Task<(bool Success, string Message)> DeletePhotoAsync(int customerId);
    Task<CustomerSettingsResponse> GetSettingsAsync(int customerId);
    Task<bool> UpdateSettingsAsync(int customerId, CustomerSettingsRequest request);
    Task<CustomerOrdersResponse> GetOrdersAsync(int customerId);
    Task<SavedFlightsResponse> GetSavedFlightsAsync(int customerId);
    Task<(bool Success, string Message, int? SavedFlightId)> SaveFlightAsync(int customerId, int flightId);
    Task<(bool Success, string Message)> RemoveSavedFlightAsync(int customerId, int savedFlightId);
    Task<(bool Success, string Message, bool? NotificationEnabled)> ToggleFlightNotificationAsync(int customerId, int savedFlightId);
    Task<(bool Success, string Message, object? Data)> AddPaymentMethodAsync(int customerId, AddPaymentMethodRequest request);
}
