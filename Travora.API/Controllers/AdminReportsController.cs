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
    [ProducesResponseType(typeof(ReportDashboardDataResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboardReportsAsync()
    {
        var result = await _reportService.GetDashboardStatsAsync();
        return Ok(result);
    }

    [HttpGet("reports")]
    [ProducesResponseType(typeof(ReportListResponseWrapper), StatusCodes.Status200OK)] 
    public async Task<IActionResult> GetReportsListAsync()
    {
        var result = await _reportService.GetReportsAsync();
        return Ok(new ReportListResponseWrapper { Reports = result });
    }

    [HttpPost("reports")]
    [ProducesResponseType(typeof(CreateReportResponseWrapper), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(ReportDetailResponseWrapper), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReportByIdAsync(int reportId)
    {
        var report = await _reportService.GetReportByIdAsync(reportId);
        if (report == null) return NotFound(new { message = "Report not found" });

        return Ok(new ReportDetailResponseWrapper
        {
            ReportId = report.ReportId,
            Name = report.ReportName,
            Type = report.ReportType.ToString(),
            PeriodStart = report.PeriodStartDate,
            PeriodEnd = report.PeriodEndDate,
            GeneratedAt = report.GeneratedAt,
            Status = string.IsNullOrEmpty(report.ReportFilePath) ? "inProgress" : "completed",
            FileUrl = report.ReportFilePath,
            AdminId = report.GeneratedByAdminId
        });
    }

    [HttpGet("reports/{reportId}/export")]
    [ProducesResponseType(typeof(ReportExportResponseWrapper), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportReportPdfAsync(int reportId)
    {
        try
        {
            var pdfUrl = await _reportService.GetReportExportUrlAsync(reportId);
            
            // Redirecting user to Cloudinary URL or return URL for frontend to open
            // Returning the URL since downloading directly might have CORS/Stream issues with API Gateway
            return Ok(new ReportExportResponseWrapper { Url = pdfUrl });
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

public class ReportListResponseWrapper
{
    public List<ReportListItemResponse> Reports { get; set; } = new();
}

public class ReportDetailResponseWrapper
{
    public int ReportId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public int? AdminId { get; set; }
}

public class ReportExportResponseWrapper
{
    public string Url { get; set; } = string.Empty;
}

public class CreateReportResponseWrapper
{
    public int ReportId { get; set; }
    public string Message { get; set; } = string.Empty;
}
