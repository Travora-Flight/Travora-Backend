using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.BackgroundJobs;

public class ReportGeneratorJob : IReportGeneratorJob
{
    private readonly ApplicationDbContext _db;
    private readonly ICloudinaryService _cloudinaryService;

    public ReportGeneratorJob(ApplicationDbContext db, ICloudinaryService cloudinaryService)
    {
        _db = db;
        _cloudinaryService = cloudinaryService;
    }

    public async Task GeneratePdfReportAsync(int reportId)
    {
        var report = await _db.Reports
            .Include(r => r.GeneratedByAdmin)
            .FirstOrDefaultAsync(r => r.ReportId == reportId);

        if (report == null)
            return;

        object reportData = null;

        // Fetch Data based on Type
        switch (report.ReportType)
        {
            case ReportType.DailyOrders:
            case ReportType.MonthlyRevenue:
                reportData = await _db.Orders
                    .AsNoTracking()
                    .Where(o => o.CreatedAt >= report.PeriodStartDate && o.CreatedAt <= report.PeriodEndDate)
                    .Select(o => new { o.OrderId, o.TotalAmount, o.OrderStatus, o.CreatedAt })
                    .ToListAsync();
                break;
            default:
                reportData = new { Message = "No specific data for this report type yet." };
                break;
        }

        // 1. Update JSON Data
        report.ReportDataJson = JsonSerializer.Serialize(reportData);
        await _db.SaveChangesAsync(); // Save JSON early

        // 2. Generate PDF
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(12));

                page.Header().Text($"{report.ReportName}")
                    .SemiBold().FontSize(20).FontColor(Colors.Blue.Darken2);

                page.Content().PaddingVertical(1, Unit.Centimetre).Column(x =>
                {
                    x.Item().Text($"Type: {report.ReportType}");
                    x.Item().Text($"Period: {report.PeriodStartDate:yyyy-MM-dd} to {report.PeriodEndDate:yyyy-MM-dd}");
                    x.Item().Text($"Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");

                    x.Item().PaddingTop(20).Text("Report Data:").SemiBold();
                    
                    // Display raw JSON in PDF for simplicity, or format a basic table
                    var jsonString = JsonSerializer.Serialize(reportData, new JsonSerializerOptions { WriteIndented = true });
                    x.Item().Text(jsonString).FontSize(10);
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        });

        var pdfBytes = document.GeneratePdf();

        // 3. Upload to Cloudinary
        using var stream = new MemoryStream(pdfBytes);
        var fileName = $"report_{report.ReportId}_{DateTime.UtcNow.Ticks}.pdf";
        
        var fileUrl = await _cloudinaryService.UploadFileAsync(stream, fileName, "travora/reports");

        // 4. Update Report Entity with the File URL
        if (!string.IsNullOrEmpty(fileUrl))
        {
            report.ReportFilePath = fileUrl;
            report.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
