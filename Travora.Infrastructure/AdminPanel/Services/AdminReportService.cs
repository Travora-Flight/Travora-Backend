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
        var total = await _db.Reports.CountAsync();
        var completed = await _db.Reports.CountAsync(r => !string.IsNullOrEmpty(r.ReportFilePath));
        var padding = await _db.Reports.CountAsync(r => string.IsNullOrEmpty(r.ReportFilePath));

        return new ReportDashboardDataResponse
        {
            Stats = new ReportStatsResponse
            {
                Total = total,
                Completed = completed,
                InProgress = padding > 0 ? padding : 0, 
                Pending = 0 // Keeping it simple for stats
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
