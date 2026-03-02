using Travora.Application.DTOs.Admin.Reports;
using Travora.Domain.Entities;

namespace Travora.Application.Interfaces;

public interface IAdminReportService
{
    Task<ReportDashboardDataResponse> GetDashboardStatsAsync();
    Task<List<ReportListItemResponse>> GetReportsAsync();
    Task<object> CreateReportAsync(CreateReportRequest request, int adminId);
    Task<Report?> GetReportByIdAsync(int reportId);
    Task<string> GetReportExportUrlAsync(int reportId);
}
