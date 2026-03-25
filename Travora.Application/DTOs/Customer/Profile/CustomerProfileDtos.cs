namespace Travora.Application.DTOs.Customer.Profile;

public class CustomerProfileResponse
{
    public int CustomerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public string Email { get; set; } = string.Empty;
    public string AccountStatus { get; set; } = string.Empty;
}

public class CustomerAccountResponse
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string DateOfBirth { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
}

public class UpdateAccountRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MobileNumber { get; set; }
    public string? Gender { get; set; }
}

public class UploadPhotoResponse
{
    public bool Success { get; set; } = true;
    public string ProfileImageUrl { get; set; } = string.Empty;
}

