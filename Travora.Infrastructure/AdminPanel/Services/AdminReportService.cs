using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Reports;
using Travora.Application.Interfaces;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminReportService : IAdminReportService
{
    private readonly ApplicationDbContext _db;

    public AdminReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ReportDashboardResponse> GetDashboardReportsAsync(DateTime? startDate, DateTime? endDate)
    {
        var query = _db.Orders.AsQueryable();

        if (startDate.HasValue) query = query.Where(o => o.CreatedAt >= startDate.Value);
        if (endDate.HasValue) query = query.Where(o => o.CreatedAt <= endDate.Value);

        var orders = await query.ToListAsync();

        var totalRevenue = orders.Where(o => o.OrderStatus == OrderStatus.Completed).Sum(o => o.TotalAmount);
        var totalOrders = orders.Count;
        var completed = orders.Count(o => o.OrderStatus == OrderStatus.Completed);
        var cancelled = orders.Count(o => o.OrderStatus == OrderStatus.Cancelled);
        var average = completed > 0 ? totalRevenue / completed : 0;

        return new ReportDashboardResponse
        {
            TotalRevenue = totalRevenue,
            TotalOrders = totalOrders,
            CompletedOrders = completed,
            CancelledOrders = cancelled,
            AverageOrderValue = Math.Round(average, 2)
        };
    }

    public async Task<List<OrderReportItem>> GetOrderReportsAsync(DateTime? startDate, DateTime? endDate, string? status)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .AsQueryable();

        if (startDate.HasValue) query = query.Where(o => o.CreatedAt >= startDate.Value);
        if (endDate.HasValue) query = query.Where(o => o.CreatedAt <= endDate.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
                query = query.Where(o => o.OrderStatus == parsedStatus);
        }

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(100) // limit for UI performance if no pagination
            .ToListAsync();

        return orders.Select(o => new OrderReportItem
        {
            OrderId = o.OrderId,
            ClientName = o.Customer != null ? $"{o.Customer.Firstname} {o.Customer.Lastname}" : "Unknown",
            ServiceType = o.PackageId > 0 ? "Package" : "Service",
            TotalAmount = o.TotalAmount,
            Status = o.OrderStatus.ToString().ToLower(),
            CreatedAt = o.CreatedAt.ToString("yyyy-MM-dd HH:mm")
        }).ToList();
    }

    public async Task<List<EmployeePerformanceItem>> GetEmployeePerformanceAsync(DateTime? startDate, DateTime? endDate)
    {
        var query = _db.OrderServices
            .Include(os => os.AssignedEmployee)
            .Where(os => os.AssignedEmployeeId != null && os.ServiceStatus == ServiceStatus.Completed)
            .AsQueryable();

        if (startDate.HasValue) query = query.Where(os => os.CreatedAt >= startDate.Value);
        if (endDate.HasValue) query = query.Where(os => os.CreatedAt <= endDate.Value);

        var stats = await query
            .GroupBy(os => os.AssignedEmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key.Value,
                CompletedTasks = g.Count()
            })
            .ToListAsync();

        var employeeIds = stats.Select(s => s.EmployeeId).ToList();
        var employees = await _db.Employees
            .Where(e => employeeIds.Contains(e.EmployeeId))
            .ToListAsync();

        var result = new List<EmployeePerformanceItem>();
        foreach (var stat in stats)
        {
            var emp = employees.FirstOrDefault(e => e.EmployeeId == stat.EmployeeId);
            if (emp != null)
            {
                result.Add(new EmployeePerformanceItem
                {
                    EmployeeId = emp.EmployeeId,
                    EmployeeName = $"{emp.Firstname} {emp.Lastname}",
                    JobRole = emp.JobRole.ToString(),
                    CompletedTasks = stat.CompletedTasks,
                    Rating = 5.0m // Mock rating, you can implement real rating logic
                });
            }
        }

        return result.OrderByDescending(r => r.CompletedTasks).ToList();
    }
}
