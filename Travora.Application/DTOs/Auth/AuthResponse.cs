namespace Travora.Application.DTOs.Auth;

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; } = 86400;
    public string Role { get; set; } = string.Empty;
    public object? UserData { get; set; }
}
