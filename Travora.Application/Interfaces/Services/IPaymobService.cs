using Travora.Application.DTOs.Payments;

namespace Travora.Application.Interfaces.Services;

public interface IPaymobService
{
    Task<PaymentInitiationResponse> InitiatePaymentAsync(int orderId, int customerId);
    Task HandleWebhookAsync(Dictionary<string, string> formData, string hmacFromPaymob);
    Task<PaymentStatusResponse> GetPaymentStatusAsync(int orderId);
}
