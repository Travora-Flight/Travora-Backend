using Travora.Application.DTOs.Customer.Auth;

namespace Travora.Application.Interfaces.Services.Customer;

public interface ICustomerAuthService
{
    Task<object> RegisterStep1Async(RegisterStep1Request request);
    Task<RegisterResponse> RegisterStep2Async(RegisterStep2Request request);
    Task<CustomerLoginResponse> LoginAsync(CustomerLoginRequest request, string ipAddress, string userAgent);
    Task<object> RefreshTokenAsync(string refreshToken);
    Task<object> ForgotPasswordAsync(string email);
    Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request);
    Task<object> ResetPasswordAsync(ResetPasswordRequest request);
    Task<object> VerifyEmailAsync(VerifyEmailRequest request);
    Task<object> ResendVerificationEmailAsync(ResendVerifyEmailRequest request);
}
