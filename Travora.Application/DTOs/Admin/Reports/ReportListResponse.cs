namespace Travora.Application.DTOs.Admin.Reports;

public class ReportListResponse
{
    public List<ReportListItemResponse> Reports { get; set; } = new();
}
