namespace Travora.Application.DTOs.Customer.Auth;

public class VerifyOtpResponse
{
    public bool Success { get; set; }
    public string ResetToken { get; set; } = string.Empty;
}
