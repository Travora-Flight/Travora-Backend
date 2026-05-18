namespace Travora.Application.DTOs.Admin.Reports;

public class ReportStatsResponse
{
    public int TotalGenerated { get; set; }
    public int DailyOrdersCount { get; set; }
    public int MonthlyReportsCount { get; set; }
}
