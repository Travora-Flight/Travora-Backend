namespace Travora.Application.DTOs.Customer.Auth;

public class RegisterResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string AccountStatus { get; set; } = string.Empty;
    public bool PassportVerified { get; set; }
    public bool RequiresManualReview { get; set; }
    public string? PassportNumber { get; set; }
}
