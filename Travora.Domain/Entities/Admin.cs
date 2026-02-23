using Travora.Domain.Common;

namespace Travora.Domain.Entities;

public class Admin : IHasTimestamps
{
    public int AdminId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsSuperAdmin { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<Employee> CreatedEmployees { get; set; } = new List<Employee>();
    public ICollection<Document> VerifiedDocuments { get; set; } = new List<Document>();
    public ICollection<Refund> ProcessedRefunds { get; set; } = new List<Refund>();
    public ICollection<Report> GeneratedReports { get; set; } = new List<Report>();
}
