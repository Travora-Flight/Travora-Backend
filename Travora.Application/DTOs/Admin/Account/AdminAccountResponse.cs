namespace Travora.Application.DTOs.Admin.Account;

public class AdminAccountResponse
{
    public int AdminId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsSuperAdmin { get; set; }
}
