namespace Travora.Application.DTOs.Customer.Auth;

public class CustomerLoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string AccountStatus { get; set; } = string.Empty;
    public bool ProfileCompleted { get; set; }
}
