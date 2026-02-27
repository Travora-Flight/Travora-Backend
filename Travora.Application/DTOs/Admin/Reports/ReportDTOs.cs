namespace Travora.Application.DTOs.Admin.Reports;

public class ReportDashboardResponse
{
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int CancelledOrders { get; set; }
    public decimal AverageOrderValue { get; set; }
}

public class OrderReportItem
{
    public int OrderId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public class EmployeePerformanceItem
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string JobRole { get; set; } = string.Empty;
    public int CompletedTasks { get; set; }
    public decimal Rating { get; set; }
}
