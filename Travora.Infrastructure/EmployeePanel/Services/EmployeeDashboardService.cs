using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Employee.Dashboard;
using Travora.Application.Interfaces.Services.Employee;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.EmployeePanel.Services;

public class EmployeeDashboardService : IEmployeeDashboardService
{
    private readonly ApplicationDbContext _db;

    public EmployeeDashboardService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<EmployeeDashboardResponse> GetDashboardAsync(int employeeId)
    {
        var employee = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        var now = DateTime.UtcNow;
        var today = now.Date;

        // Stats
        var newTasks = await _db.OrderServices
            .CountAsync(os => os.AssignedEmployeeId == employeeId && os.ServiceStatus == ServiceStatus.Assigned);

        var ongoingTasks = await _db.OrderServices
            .CountAsync(os => os.AssignedEmployeeId == employeeId && os.ServiceStatus == ServiceStatus.InProgress);

        var completedTasks = await _db.OrderServices
            .CountAsync(os => os.AssignedEmployeeId == employeeId
                && os.ServiceStatus == ServiceStatus.Completed);

        // Current in-progress tasks
        var currentTasks = await _db.OrderServices
            .Where(os => os.AssignedEmployeeId == employeeId && os.ServiceStatus == ServiceStatus.InProgress)
            .Include(os => os.Order).ThenInclude(o => o.PickupLocation)
            .Include(os => os.PackageService).ThenInclude(ps => ps.Service)
            .Select(os => new CurrentTaskItemDto
        {
            OrderServiceId = os.OrderServiceId,
            Status = os.ServiceStatus.ToString(),
            Type = os.PackageService.Service.ServiceName,
            Location = os.Order.PickupLocation.City + ", " + os.Order.PickupLocation.StreetAddress,
                ScheduledTime = os.ScheduledStartTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
            })
            .ToListAsync();

        // New assigned pending requests (top 10)
        var newAssignedRequestsList = await _db.OrderServices
            .Where(os => os.AssignedEmployeeId == employeeId && os.ServiceStatus == ServiceStatus.Assigned)
            .Include(os => os.Order).ThenInclude(o => o.PickupLocation)
            .Include(os => os.PackageService).ThenInclude(ps => ps.Service)
            .OrderBy(os => os.ScheduledStartTime)
            .Take(10)
            .ToListAsync();

        var newAssignedRequests = newAssignedRequestsList.Select(os => new NewAssignedRequestDto
        {
            OrderServiceId = os.OrderServiceId,
            Status = os.ServiceStatus.ToString(),
            Type = os.PackageService?.Service?.ServiceName ?? string.Empty,
            Location = os.Order.PickupLocation.City + ", " + os.Order.PickupLocation.StreetAddress,
            ScheduledTime = os.ScheduledStartTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ScheduledDate = os.ScheduledStartTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            CanStart = os.CanEmployeeStart(now)
        }).ToList();

        return new EmployeeDashboardResponse
        {
            Greeting = $"Hi, {employee.Firstname}",
            Stats = new EmployeeStatsDto
            {
                NewTasks = newTasks,
                OngoingTasks = ongoingTasks,
                CompletedTasks = completedTasks
            },
            CurrentTasks = currentTasks,
            NewAssignedRequests = newAssignedRequests
        };
    }
}
