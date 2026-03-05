namespace Travora.Application.DTOs.Auth;

public class ChangePasswordFirstLoginRequest
{
    public string TempPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}
