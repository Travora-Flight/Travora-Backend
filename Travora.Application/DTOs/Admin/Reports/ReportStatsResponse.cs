namespace Travora.Application.DTOs.Admin.Reports;

public class ReportStatsResponse
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int InProgress { get; set; }
    public int Pending { get; set; }
}
