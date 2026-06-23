using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travora.Application.Interfaces;
using Travora.Application.DTOs.Employee.Location;
using Travora.Application.Interfaces.Hubs;
using Travora.Application.Interfaces.Services.Employee;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.EmployeePanel.Services;

public class EmployeeLocationService : IEmployeeLocationService
{
    private readonly ApplicationDbContext _db;
    private readonly IUpstashRedisService _redis;
    private readonly ILiveTrackingHubService _liveTrackingHub;

    public EmployeeLocationService(
        ApplicationDbContext db,
        IUpstashRedisService redis,
        ILiveTrackingHubService liveTrackingHub)
    {
        _db = db;
        _redis = redis;
        _liveTrackingHub = liveTrackingHub;
    }

    public async Task<DriverLocationResponse> UpdateLocationAsync(int employeeId, DriverLocationRequest request)
    {
        var employee = await _db.Employees.FindAsync(employeeId)
            ?? throw new KeyNotFoundException("Employee not found");

        if (employee.JobRole != JobRole.Driver)
            throw new UnauthorizedAccessException("GPS tracking is for Drivers only");

        // Check if employee is within shift hours
        var now = DateTime.UtcNow.TimeOfDay;
        var isWithinShift = employee.ShiftType switch
        {
            ShiftType.Morning => now >= TimeSpan.FromHours(8) && now <= TimeSpan.FromHours(16),
            ShiftType.Evening => now >= TimeSpan.FromHours(16) && now <= TimeSpan.FromHours(24),
            ShiftType.Night => now >= TimeSpan.Zero && now <= TimeSpan.FromHours(8),
            ShiftType.rotating => true, // Always on
            _ => true
        };

        if (!isWithinShift)
        {
            return new DriverLocationResponse
            {
                Success = true,
                SavedToDb = false,
                Status = "off_shift"
            };
        }

        var activeOrderServiceId = request.OrderServiceId;
        if (!activeOrderServiceId.HasValue)
        {
            // Fail-safe: If mobile doesn't supply OrderServiceId, check DB for any InProgress task for this employee
            var dbActiveTask = await _db.OrderServices
                .Where(os => os.AssignedEmployeeId == employeeId && os.ServiceStatus == ServiceStatus.InProgress)
                .Select(os => (int?)os.OrderServiceId)
                .FirstOrDefaultAsync();
            if (dbActiveTask.HasValue)
            {
                activeOrderServiceId = dbActiveTask;
            }
        }

        var status = activeOrderServiceId.HasValue ? "on_service" : "available";

        // 1) Save to Redis (TTL: 3 minutes)
        var redisValue = JsonSerializer.Serialize(new
        {
            latitude = request.Latitude,
            longitude = request.Longitude,
            speed = request.SpeedKmh,
            heading = request.HeadingDegrees,
            isMoving = request.IsMoving,
            updatedAt = request.TrackedAtUtc,
            orderServiceId = activeOrderServiceId,
            status
        });
        await _redis.SetAsync($"employee:{employeeId}:last_location", redisValue, TimeSpan.FromMinutes(3));

        // 2) Save to DB if activeOrderServiceId exists and > 30s since last record
        var savedToDb = false;
        if (activeOrderServiceId.HasValue)
        {
            var lastRecord = await _db.DriverTrackings
                .Where(dt => dt.DriverId == employeeId && dt.OrderServiceId == activeOrderServiceId.Value)
                .OrderByDescending(dt => dt.TrackedAt)
                .FirstOrDefaultAsync();

            if (lastRecord == null || (DateTime.UtcNow - lastRecord.TrackedAt).TotalSeconds >= 30)
            {
                _db.DriverTrackings.Add(new DriverTracking
                {
                    DriverId = employeeId,
                    OrderServiceId = activeOrderServiceId.Value,
                    GpsLatitude = request.Latitude,
                    GpsLongitude = request.Longitude,
                    SpeedKmh = request.SpeedKmh,
                    HeadingDegrees = request.HeadingDegrees,
                    AccuracyMeters = request.AccuracyMeters,
                    IsMoving = request.IsMoving,
                    IsOnline = true,
                    TrackedAt = request.TrackedAtUtc
                });
                await _db.SaveChangesAsync();
                savedToDb = true;
            }
        }

        // 3) SignalR to Admin Live Tracker
        string currentTask = "";
        if (activeOrderServiceId.HasValue)
        {
            var os = await _db.OrderServices
                .Include(x => x.PackageService).ThenInclude(ps => ps.Service)
                .Include(x => x.Order).ThenInclude(o => o.PickupLocation)
                .FirstOrDefaultAsync(x => x.OrderServiceId == activeOrderServiceId.Value);
            if (os != null)
                currentTask = $"{os.PackageService.Service.ServiceName} - {os.Order.PickupLocation.City}";
        }

        await _liveTrackingHub.SendLocationUpdate(new
        {
            employeeId,
            name = $"{employee.Firstname} {employee.Lastname}",
            latitude = request.Latitude,
            longitude = request.Longitude,
            status,
            currentTask,
            lastUpdated = "Just now"
        });

        return new DriverLocationResponse
        {
            Success = true,
            SavedToDb = savedToDb,
            Status = status
        };
    }
}
