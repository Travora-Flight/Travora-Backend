using Travora.Application.DTOs.Payments;

namespace Travora.Application.Interfaces.Services;

public interface IPaymentMethodService
{
    Task<PaymentMethodsResponse> GetCustomerPaymentMethodsAsync(int customerId);
    Task<bool> SetDefaultPaymentMethodAsync(int customerId, int paymentMethodId);
    Task<(bool Success, string Message)> DeletePaymentMethodAsync(int customerId, int paymentMethodId);
}
