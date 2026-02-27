using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class LoginLog
{
    public int LogId { get; set; }
    public UserType UserType { get; set; }
    public DateTime LoginTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime? LogoutTimestamp { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public LoginStatus LoginStatus { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;

    // Foreign keys
    public int? AdminId { get; set; }
    public int? CustomerId { get; set; }
    public int? EmployeeId { get; set; }

    // Navigation properties
    public Admin? Admin { get; set; }
    public Customer? Customer { get; set; }
    public Employee? Employee { get; set; }
}
