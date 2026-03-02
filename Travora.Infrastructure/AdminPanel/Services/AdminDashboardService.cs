using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Dashboard;
using Travora.Application.Interfaces;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminDashboardService : IAdminDashboardService
{
    private readonly ApplicationDbContext _db;

    public AdminDashboardService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardStatsResponse> GetDashboardStatsAsync()
    {
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);

        // الأساسيات
        var allEmployees = await _db.Employees.CountAsync();
        var newRequests = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Pending);
        var currentRequests = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.InProgress);
        var doneRequests = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Completed);

        // نشاط آخر 7 أيام
        var ordersLast7Days = await _db.Orders
            .Where(o => o.CreatedAt >= sevenDaysAgo)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Completed = g.Count(o => o.OrderStatus == OrderStatus.Completed),
                NewReqs = g.Count(o => o.CreatedAt.Date == g.Key),
                Ongoing = g.Count(o => o.OrderStatus == OrderStatus.InProgress)
            })
            .ToListAsync();

        var weeklyActivity = new List<WeeklyActivityItem>();
        for (int i = 6; i >= 0; i--) // من الأقدم للأحدث خلال الأسبوع
        {
            var day = now.Date.AddDays(-i);
            var stats = ordersLast7Days.FirstOrDefault(o => o.Date == day);
            weeklyActivity.Add(new WeeklyActivityItem
            {
                Day = day.ToString("ddd"), // e.g. Mon, Tue
                Completed = stats?.Completed ?? 0,
                NewRequests = stats?.NewReqs ?? 0,
                Ongoing = stats?.Ongoing ?? 0
            });
        }

        return new DashboardStatsResponse
        {
            AllEmployees = allEmployees,
            AllEmployeesGrowth = 0, // Mock: تحتاج مقارنة بالشهر الماضي إذا مطلوب
            NewRequests = newRequests,
            NewRequestsGrowth = 0,
            CurrentRequests = currentRequests,
            CurrentRequestsChange = 0,
            DoneRequests = doneRequests,
            DoneRequestsGrowth = 0,
            WeeklyActivity = weeklyActivity
        };
    }
}
