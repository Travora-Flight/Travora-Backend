using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Admin.Reports;
using Travora.Application.Interfaces;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "admin")]
[Tags("AdminReports")]
public class AdminReportsController : ControllerBase
{
    private readonly IAdminReportService _reportService;

    public AdminReportsController(IAdminReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("dashboard/reports")]
    public async Task<IActionResult> GetDashboardReportsAsync()
    {
        var result = await _reportService.GetDashboardStatsAsync();
        return Ok(result);
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReportsListAsync()
    {
        var result = await _reportService.GetReportsAsync();
        return Ok(new { reports = result });
    }

    [HttpPost("reports")]
    public async Task<IActionResult> CreateReportAsync([FromForm] CreateReportRequest request)
    {
        // Try getting admin ID from NameIdentifier or sub claims since different JWT generators use different claims
        var adminIdClaim = User.FindFirst("adminId")?.Value 
                        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                        ?? User.FindFirst("sub")?.Value 
                        ?? User.FindFirst("id")?.Value;

        if (string.IsNullOrEmpty(adminIdClaim) || !int.TryParse(adminIdClaim, out int adminId))
        {
            return Unauthorized(new { message = "Invalid admin token" });
        }

        var result = await _reportService.CreateReportAsync(request, adminId);
        return Ok(result);
    }

    [HttpGet("reports/{reportId}")]
    public async Task<IActionResult> GetReportByIdAsync(int reportId)
    {
        var report = await _reportService.GetReportByIdAsync(reportId);
        if (report == null) return NotFound(new { message = "Report not found" });

        return Ok(new
        {
            reportId = report.ReportId,
            name = report.ReportName,
            type = report.ReportType.ToString(),
            periodStart = report.PeriodStartDate,
            periodEnd = report.PeriodEndDate,
            generatedAt = report.GeneratedAt,
            status = string.IsNullOrEmpty(report.ReportFilePath) ? "inProgress" : "completed",
            fileUrl = report.ReportFilePath,
            adminId = report.GeneratedByAdminId
        });
    }

    [HttpGet("reports/{reportId}/export")]
    public async Task<IActionResult> ExportReportPdfAsync(int reportId)
    {
        try
        {
            var pdfUrl = await _reportService.GetReportExportUrlAsync(reportId);
            
            // Redirecting user to Cloudinary URL or return URL for frontend to open
            // Returning the URL since downloading directly might have CORS/Stream issues with API Gateway
            return Ok(new { url = pdfUrl });
        }
        catch (ApplicationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
