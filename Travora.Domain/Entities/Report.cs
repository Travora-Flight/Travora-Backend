using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Report : IHasTimestamps
{
    public int ReportId { get; set; }
    public ReportType ReportType { get; set; }
    public string ReportName { get; set; } = string.Empty;
    public DateTime PeriodStartDate { get; set; }
    public DateTime PeriodEndDate { get; set; }
    public string ReportFilePath { get; set; } = string.Empty;
    public string? ReportDataJson { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int GeneratedByAdminId { get; set; }

    // Navigation properties
    public Admin GeneratedByAdmin { get; set; } = null!;
}
