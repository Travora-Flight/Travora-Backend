namespace Travora.Application.DTOs.Admin.Reports;

public class ReportListItemResponse
{
    public int ReportId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
