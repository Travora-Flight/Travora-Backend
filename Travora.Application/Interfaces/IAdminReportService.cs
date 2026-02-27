using Travora.Application.DTOs.Admin.Reports;

namespace Travora.Application.Interfaces;

public interface IAdminReportService
{
    Task<ReportDashboardResponse> GetDashboardReportsAsync(DateTime? startDate, DateTime? endDate);
    Task<List<OrderReportItem>> GetOrderReportsAsync(DateTime? startDate, DateTime? endDate, string? status);
    Task<List<EmployeePerformanceItem>> GetEmployeePerformanceAsync(DateTime? startDate, DateTime? endDate);
}
