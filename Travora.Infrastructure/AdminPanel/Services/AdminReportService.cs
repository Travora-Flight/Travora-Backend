using Hangfire;
using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Reports;
using Travora.Application.Interfaces;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminReportService : IAdminReportService
{
    private readonly ApplicationDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public AdminReportService(ApplicationDbContext db, IBackgroundJobClient backgroundJobClient)
    {
        _db = db;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task<ReportDashboardDataResponse> GetDashboardStatsAsync()
    {
        var totalGenerated = await _db.Reports.CountAsync();
        var dailyOrdersCount = await _db.Reports.CountAsync(r => r.ReportType == ReportType.DailyOrders);
        
        var currentMonthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthlyReportsCount = await _db.Reports.CountAsync(r => r.CreatedAt >= currentMonthStart);

        return new ReportDashboardDataResponse
        {
            Stats = new ReportStatsResponse
            {
                TotalGenerated = totalGenerated,
                DailyOrdersCount = dailyOrdersCount,
                MonthlyReportsCount = monthlyReportsCount
            }
        };
    }

    public async Task<List<ReportListItemResponse>> GetReportsAsync()
    {
        return await _db.Reports
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReportListItemResponse
            {
                ReportId = r.ReportId,
                Name = r.ReportName,
                Type = r.ReportType.ToString(),
                Date = r.CreatedAt.ToString("MMM dd, yyyy"),
                Status = string.IsNullOrEmpty(r.ReportFilePath) ? "inProgress" : "completed"
            })
            .ToListAsync();
    }

    public async Task<object> CreateReportAsync(CreateReportRequest request, int adminId)
    {
        var report = new Report
        {
            ReportName = request.ReportName,
            ReportType = request.ReportType,
            PeriodStartDate = request.StartDate,
            PeriodEndDate = request.EndDate,
            GeneratedByAdminId = adminId
        };

        _db.Reports.Add(report);
        await _db.SaveChangesAsync();

        // Enqueue Job for processing in background
        _backgroundJobClient.Enqueue<IReportGeneratorJob>(job => job.GeneratePdfReportAsync(report.ReportId));

        return new
        {
            success = true,
            reportId = report.ReportId,
            message = "Report generation started in background."
        };
    }

    public async Task<Report?> GetReportByIdAsync(int reportId)
    {
        return await _db.Reports
            .Include(r => r.GeneratedByAdmin)
            .FirstOrDefaultAsync(r => r.ReportId == reportId);
    }

    public async Task<string> GetReportExportUrlAsync(int reportId)
    {
        var report = await _db.Reports.FindAsync(reportId);
        if (report == null)
            throw new KeyNotFoundException("Report not found");

        if (string.IsNullOrEmpty(report.ReportFilePath))
            throw new ApplicationException("Report is still generating or failed. No file available yet.");

        return report.ReportFilePath;
    }
}
