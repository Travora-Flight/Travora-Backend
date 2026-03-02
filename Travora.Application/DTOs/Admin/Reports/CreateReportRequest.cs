namespace Travora.Application.DTOs.Admin.Reports;

public class CreateReportRequest
{
    public string ReportName { get; set; } = string.Empty;
    public Travora.Domain.Enums.ReportType ReportType { get; set; } 
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
