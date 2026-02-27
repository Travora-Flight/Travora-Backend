using Travora.Application.DTOs.Auth;

namespace Travora.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> LoginAdminAsync(string email, string password, string ipAddress, string userAgent);
    Task<AuthResponse> LoginEmployeeAsync(string email, string password, string ipAddress, string userAgent);
    Task<AuthResponse> LoginCustomerAsync(string email, string password, string ipAddress, string userAgent);
    Task<AuthResponse> RefreshTokenAsync(string refreshToken);
    Task LogoutAsync(string refreshToken);
}
