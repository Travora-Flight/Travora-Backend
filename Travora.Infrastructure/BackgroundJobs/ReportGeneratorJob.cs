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

public class FinancialReportModel
{
    public decimal TotalRevenue { get; set; }
    public decimal TotalRefunds { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal BaggageHandlingRevenue { get; set; }
    public decimal CustomsRevenue { get; set; }
    public decimal FlightRevenue { get; set; }
}

public class TransactionLedgerItem
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DailyOrderReportItem
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
}

public class CustomsSummaryItem
{
    public int CustomsId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomsType { get; set; } = string.Empty;
    public decimal TotalDeclaredValue { get; set; }
    public decimal TotalCustomsFee { get; set; }
    public string Date { get; set; } = string.Empty;
}

public class EmployeePerformanceItem
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string JobRole { get; set; } = string.Empty;
    public int TotalAssigned { get; set; }
    public int TotalCompleted { get; set; }
    public double SuccessRate { get; set; }
}

public class CustomerFeedbackItem
{
    public int FeedbackId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int OverallRating { get; set; }
    public int ServiceQualityRating { get; set; }
    public int PunctualityRating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
}

public class RefundAnalysisItem
{
    public int RefundId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
    public string RefundStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
}

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
                reportData = await _db.Orders
                    .AsNoTracking()
                    .Include(o => o.Customer)
                    .Where(o => o.CreatedAt >= report.PeriodStartDate && o.CreatedAt <= report.PeriodEndDate)
                    .OrderByDescending(o => o.CreatedAt)
                    .Select(o => new DailyOrderReportItem
                    {
                        OrderId = o.OrderId,
                        CustomerName = o.Customer != null ? o.Customer.Firstname + " " + o.Customer.Lastname : "Unknown",
                        TotalAmount = o.TotalAmount,
                        OrderStatus = o.OrderStatus.ToString(),
                        Date = o.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToListAsync();
                break;

            case ReportType.Financial:
                {
                    var payments = await _db.Payments
                        .Include(p => p.Invoice)
                        .Where(p => p.PaymentDate >= report.PeriodStartDate && p.PaymentDate <= report.PeriodEndDate && p.PaymentStatus == PaymentStatus.Completed)
                        .ToListAsync();

                    var refunds = await _db.Refunds
                        .Where(r => r.ProcessedAt >= report.PeriodStartDate && r.ProcessedAt <= report.PeriodEndDate && r.RefundStatus == RefundStatus.Processed)
                        .ToListAsync();

                    var totalRevenue = payments.Sum(p => p.Amount);
                    var totalRefunds = refunds.Sum(r => r.RefundAmount);
                    var netRevenue = totalRevenue - totalRefunds;

                    var packageFeeRevenue = payments.Sum(p => p.Invoice?.PackageFee ?? 0);
                    var customsFeeRevenue = payments.Sum(p => p.Invoice?.CustomsFee ?? 0);
                    var flightRevenue = totalRevenue - packageFeeRevenue - customsFeeRevenue;

                    reportData = new FinancialReportModel
                    {
                        TotalRevenue = totalRevenue,
                        TotalRefunds = totalRefunds,
                        NetRevenue = netRevenue,
                        BaggageHandlingRevenue = packageFeeRevenue,
                        CustomsRevenue = customsFeeRevenue,
                        FlightRevenue = flightRevenue >= 0 ? flightRevenue : 0
                    };
                }
                break;

            case ReportType.CustomsSummary:
                reportData = await _db.CustomsDeclarations
                    .AsNoTracking()
                    .Include(c => c.Order)
                        .ThenInclude(o => o.Customer)
                    .Where(c => c.CreatedAt >= report.PeriodStartDate && c.CreatedAt <= report.PeriodEndDate)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => new CustomsSummaryItem
                    {
                        CustomsId = c.CustomsId,
                        CustomerName = c.Order != null && c.Order.Customer != null ? c.Order.Customer.Firstname + " " + c.Order.Customer.Lastname : "Unknown",
                        CustomsType = c.CustomsType.ToString(),
                        TotalDeclaredValue = c.TotalDeclaredValue,
                        TotalCustomsFee = c.TotalCustomsFee,
                        Date = c.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToListAsync();
                break;

            case ReportType.EmployeePerformance:
                {
                    var emps = await _db.Employees
                        .AsNoTracking()
                        .Include(e => e.AssignedOrderServices)
                        .Where(e => e.IsActive && !e.IsDeleted)
                        .ToListAsync();

                    reportData = emps.Select(e =>
                    {
                        var assigned = e.AssignedOrderServices.Count;
                        var completed = e.AssignedOrderServices.Count(os => os.ServiceStatus == ServiceStatus.Completed);
                        var rate = assigned > 0 ? Math.Round((double)completed / assigned * 100, 2) : 0.0;

                        return new EmployeePerformanceItem
                        {
                            EmployeeId = e.EmployeeId,
                            EmployeeName = e.Firstname + " " + e.Lastname,
                            JobRole = e.JobRole.ToString(),
                            TotalAssigned = assigned,
                            TotalCompleted = completed,
                            SuccessRate = rate
                        };
                    })
                    .OrderByDescending(x => x.TotalCompleted)
                    .ToList();
                }
                break;

            case ReportType.CustomerFeedback:
                reportData = await _db.Feedbacks
                    .AsNoTracking()
                    .Include(f => f.Customer)
                    .Where(f => f.CreatedAt >= report.PeriodStartDate && f.CreatedAt <= report.PeriodEndDate)
                    .OrderByDescending(f => f.CreatedAt)
                    .Select(f => new CustomerFeedbackItem
                    {
                        FeedbackId = f.FeedbackId,
                        CustomerName = f.Customer != null ? f.Customer.Firstname + " " + f.Customer.Lastname : "Unknown",
                        OverallRating = f.Rating,
                        ServiceQualityRating = f.ServiceQualityRating,
                        PunctualityRating = f.PunctualityRating,
                        Comment = f.Comment,
                        Date = f.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToListAsync();
                break;

            case ReportType.RefundAnalysis:
                reportData = await _db.Refunds
                    .AsNoTracking()
                    .Include(r => r.Order)
                        .ThenInclude(o => o.Customer)
                    .Where(r => r.CreatedAt >= report.PeriodStartDate && r.CreatedAt <= report.PeriodEndDate)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => new RefundAnalysisItem
                    {
                        RefundId = r.RefundId,
                        CustomerName = r.Order != null && r.Order.Customer != null ? r.Order.Customer.Firstname + " " + r.Order.Customer.Lastname : "Unknown",
                        RefundAmount = r.RefundAmount,
                        RefundStatus = r.RefundStatus.ToString(),
                        Reason = r.Reason,
                        Date = r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToListAsync();
                break;

            case ReportType.TransactionsLedger:
                {
                    reportData = await _db.Payments
                        .Include(p => p.Invoice)
                            .ThenInclude(i => i.Order)
                                .ThenInclude(o => o.Customer)
                        .Include(p => p.PaymentMethod)
                        .Where(p => p.PaymentDate >= report.PeriodStartDate && p.PaymentDate <= report.PeriodEndDate)
                        .OrderByDescending(p => p.PaymentDate)
                        .Select(p => new TransactionLedgerItem
                        {
                            InvoiceNumber = p.Invoice != null ? p.Invoice.InvoiceNumber : "N/A",
                            Date = p.PaymentDate.ToString("yyyy-MM-dd HH:mm:ss"),
                            CustomerName = p.Invoice != null && p.Invoice.Order != null && p.Invoice.Order.Customer != null 
                                ? p.Invoice.Order.Customer.Firstname + " " + p.Invoice.Order.Customer.Lastname 
                                : "Unknown",
                            PaymentMethod = p.PaymentMethod != null ? p.PaymentMethod.CardBrand : p.PaymentGateway,
                            Amount = p.Amount,
                            Status = p.PaymentStatus.ToString()
                        })
                        .ToListAsync();
                }
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
                    x.Item().Text($"Report Type: {report.ReportType}").FontSize(14).SemiBold();
                    x.Item().Text($"Period: {report.PeriodStartDate:yyyy-MM-dd} to {report.PeriodEndDate:yyyy-MM-dd}");
                    x.Item().Text($"Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");

                    if (report.ReportType == ReportType.Financial && reportData is FinancialReportModel finData)
                    {
                        x.Item().PaddingTop(20).Text("Financial Summary Overview").SemiBold().FontSize(16).FontColor(Colors.Blue.Darken2);
                        
                        x.Item().PaddingVertical(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(3);
                            });
                            
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Total Revenue").Bold();
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text($"{finData.TotalRevenue:N2} EGP").Bold().FontColor(Colors.Green.Darken2);
                            
                            table.Cell().Padding(5).Text("Total Refunds").Bold();
                            table.Cell().Padding(5).Text($"{finData.TotalRefunds:N2} EGP").Bold().FontColor(Colors.Red.Darken2);
                            
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Net Revenue").Bold();
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text($"{finData.NetRevenue:N2} EGP").Bold().FontColor(Colors.Blue.Darken3);
                        });

                        x.Item().PaddingTop(15).Text("Revenue Breakdown by Service Type").SemiBold().FontSize(14).FontColor(Colors.Blue.Darken2);
                        x.Item().PaddingVertical(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(3);
                            });
                            
                            table.Cell().Padding(5).Text("Baggage Handling (Door-to-Door)");
                            table.Cell().Padding(5).Text($"{finData.BaggageHandlingRevenue:N2} EGP");
                            
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Customs Fees");
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text($"{finData.CustomsRevenue:N2} EGP");
                            
                            table.Cell().Padding(5).Text("Flight Bookings");
                            table.Cell().Padding(5).Text($"{finData.FlightRevenue:N2} EGP");
                        });
                    }
                    else if (report.ReportType == ReportType.DailyOrders && reportData is List<DailyOrderReportItem> dailyOrders)
                    {
                        x.Item().PaddingTop(20).Text("Daily Orders Report").SemiBold().FontSize(16).FontColor(Colors.Blue.Darken2);
                        
                        x.Item().PaddingVertical(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2); // Order ID
                                columns.RelativeColumn(3); // Date
                                columns.RelativeColumn(4); // Customer
                                columns.RelativeColumn(3); // Amount
                                columns.RelativeColumn(3); // Status
                            });
                            
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Order ID").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Date").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Customer").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Amount").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Status").Bold().FontColor(Colors.White);
                            });
                            
                            bool alternate = false;
                            foreach (var item in dailyOrders)
                            {
                                var bg = alternate ? Colors.Grey.Lighten4 : Colors.White;
                                alternate = !alternate;
                                
                                table.Cell().Background(bg).Padding(5).Text($"#{item.OrderId}").FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.Date).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.CustomerName).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text($"{item.TotalAmount:N2} EGP").FontSize(10).Bold();
                                
                                var statusColor = item.OrderStatus == "Confirmed" || item.OrderStatus == "Completed" ? Colors.Green.Darken2 : Colors.Grey.Darken2;
                                table.Cell().Background(bg).Padding(5).Text(item.OrderStatus).FontSize(10).Bold().FontColor(statusColor);
                            }
                        });
                    }
                    else if (report.ReportType == ReportType.CustomsSummary && reportData is List<CustomsSummaryItem> customsItems)
                    {
                        x.Item().PaddingTop(20).Text("Customs Summary Report").SemiBold().FontSize(16).FontColor(Colors.Blue.Darken2);
                        
                        x.Item().PaddingVertical(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2); // Customs ID
                                columns.RelativeColumn(4); // Customer
                                columns.RelativeColumn(3); // Type
                                columns.RelativeColumn(3); // Decl. Value
                                columns.RelativeColumn(3); // Customs Fee
                                columns.RelativeColumn(3); // Date
                            });
                            
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Customs ID").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Customer").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Type").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Decl. Value").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Customs Fee").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Date").Bold().FontColor(Colors.White);
                            });
                            
                            bool alternate = false;
                            foreach (var item in customsItems)
                            {
                                var bg = alternate ? Colors.Grey.Lighten4 : Colors.White;
                                alternate = !alternate;
                                
                                table.Cell().Background(bg).Padding(5).Text($"#{item.CustomsId}").FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.CustomerName).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.CustomsType).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text($"{item.TotalDeclaredValue:N2}").FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text($"{item.TotalCustomsFee:N2} EGP").FontSize(10).Bold().FontColor(Colors.Red.Darken2);
                                table.Cell().Background(bg).Padding(5).Text(item.Date).FontSize(10);
                            }
                        });
                    }
                    else if (report.ReportType == ReportType.EmployeePerformance && reportData is List<EmployeePerformanceItem> empItems)
                    {
                        x.Item().PaddingTop(20).Text("Employee Performance Report").SemiBold().FontSize(16).FontColor(Colors.Blue.Darken2);
                        
                        x.Item().PaddingVertical(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2); // Employee ID
                                columns.RelativeColumn(4); // Employee Name
                                columns.RelativeColumn(3); // Job Role
                                columns.RelativeColumn(2); // Assigned
                                columns.RelativeColumn(2); // Completed
                                columns.RelativeColumn(2); // Success Rate
                            });
                            
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Emp ID").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Employee Name").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Job Role").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Assigned").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Completed").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Success Rate").Bold().FontColor(Colors.White);
                            });
                            
                            bool alternate = false;
                            foreach (var item in empItems)
                            {
                                var bg = alternate ? Colors.Grey.Lighten4 : Colors.White;
                                alternate = !alternate;
                                
                                table.Cell().Background(bg).Padding(5).Text($"#{item.EmployeeId}").FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.EmployeeName).FontSize(10).Bold();
                                table.Cell().Background(bg).Padding(5).Text(item.JobRole).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.TotalAssigned.ToString()).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.TotalCompleted.ToString()).FontSize(10);
                                
                                var rateColor = item.SuccessRate >= 80 ? Colors.Green.Darken2 : (item.SuccessRate >= 50 ? Colors.Orange.Darken2 : Colors.Red.Darken2);
                                table.Cell().Background(bg).Padding(5).Text($"{item.SuccessRate}%").FontSize(10).Bold().FontColor(rateColor);
                            }
                        });
                    }
                    else if (report.ReportType == ReportType.CustomerFeedback && reportData is List<CustomerFeedbackItem> feedbackItems)
                    {
                        x.Item().PaddingTop(20).Text("Customer Feedback Report").SemiBold().FontSize(16).FontColor(Colors.Blue.Darken2);
                        
                        x.Item().PaddingVertical(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); // Customer Name
                                columns.RelativeColumn(2); // Rating
                                columns.RelativeColumn(2); // Quality Rating
                                columns.RelativeColumn(2); // Punctuality Rating
                                columns.RelativeColumn(5); // Comment
                                columns.RelativeColumn(3); // Date
                            });
                            
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Customer").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Rating").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Quality").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Punctuality").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Comment").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Date").Bold().FontColor(Colors.White);
                            });
                            
                            bool alternate = false;
                            foreach (var item in feedbackItems)
                            {
                                var bg = alternate ? Colors.Grey.Lighten4 : Colors.White;
                                alternate = !alternate;
                                
                                table.Cell().Background(bg).Padding(5).Text(item.CustomerName).FontSize(9);
                                table.Cell().Background(bg).Padding(5).Text($"{item.OverallRating}/5").FontSize(9).Bold().FontColor(item.OverallRating >= 4 ? Colors.Green.Darken2 : Colors.Orange.Darken2);
                                table.Cell().Background(bg).Padding(5).Text($"{item.ServiceQualityRating}/5").FontSize(9);
                                table.Cell().Background(bg).Padding(5).Text($"{item.PunctualityRating}/5").FontSize(9);
                                table.Cell().Background(bg).Padding(5).Text(item.Comment).FontSize(9);
                                table.Cell().Background(bg).Padding(5).Text(item.Date).FontSize(9);
                            }
                        });
                    }
                    else if (report.ReportType == ReportType.RefundAnalysis && reportData is List<RefundAnalysisItem> refundItems)
                    {
                        x.Item().PaddingTop(20).Text("Refund Analysis Report").SemiBold().FontSize(16).FontColor(Colors.Blue.Darken2);
                        
                        x.Item().PaddingVertical(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2); // Refund ID
                                columns.RelativeColumn(3); // Customer
                                columns.RelativeColumn(3); // Amount
                                columns.RelativeColumn(3); // Status
                                columns.RelativeColumn(4); // Reason
                                columns.RelativeColumn(3); // Date
                            });
                            
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Refund ID").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Customer").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Amount").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Status").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Reason").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Date").Bold().FontColor(Colors.White);
                            });
                            
                            bool alternate = false;
                            foreach (var item in refundItems)
                            {
                                var bg = alternate ? Colors.Grey.Lighten4 : Colors.White;
                                alternate = !alternate;
                                
                                table.Cell().Background(bg).Padding(5).Text($"#{item.RefundId}").FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.CustomerName).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text($"{item.RefundAmount:N2} EGP").FontSize(10).Bold().FontColor(Colors.Red.Darken2);
                                
                                var statusColor = item.RefundStatus == "Processed" || item.RefundStatus == "Completed" ? Colors.Green.Darken2 : Colors.Grey.Darken2;
                                table.Cell().Background(bg).Padding(5).Text(item.RefundStatus).FontSize(10).Bold().FontColor(statusColor);
                                table.Cell().Background(bg).Padding(5).Text(item.Reason).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.Date).FontSize(10);
                            }
                        });
                    }
                    else if (report.ReportType == ReportType.TransactionsLedger && reportData is List<TransactionLedgerItem> ledgerItems)
                    {
                        x.Item().PaddingTop(20).Text("Detailed Transactions Ledger").SemiBold().FontSize(16).FontColor(Colors.Blue.Darken2);
                        
                        x.Item().PaddingVertical(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2); // Invoice
                                columns.RelativeColumn(3); // Date
                                columns.RelativeColumn(3); // Customer
                                columns.RelativeColumn(2); // Method
                                columns.RelativeColumn(2); // Amount
                                columns.RelativeColumn(2); // Status
                            });
                            
                            // Header
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Invoice").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Date").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Customer").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Method").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Amount").Bold().FontColor(Colors.White);
                                header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Status").Bold().FontColor(Colors.White);
                            });
                            
                            // Rows
                            bool alternate = false;
                            foreach (var item in ledgerItems)
                            {
                                var bg = alternate ? Colors.Grey.Lighten4 : Colors.White;
                                alternate = !alternate;
                                
                                table.Cell().Background(bg).Padding(5).Text(item.InvoiceNumber).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.Date).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.CustomerName).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text(item.PaymentMethod).FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text($"{item.Amount:N2}").FontSize(10).Bold();
                                
                                var statusColor = item.Status == "Completed" ? Colors.Green.Darken2 : (item.Status == "Refunded" ? Colors.Red.Darken2 : Colors.Grey.Darken2);
                                table.Cell().Background(bg).Padding(5).Text(item.Status).FontSize(10).Bold().FontColor(statusColor);
                            }
                        });
                    }
                    else
                    {
                        x.Item().PaddingTop(20).Text("Report Data:").SemiBold();
                        var jsonString = JsonSerializer.Serialize(reportData, new JsonSerializerOptions { WriteIndented = true });
                        x.Item().Text(jsonString).FontSize(10);
                    }
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
