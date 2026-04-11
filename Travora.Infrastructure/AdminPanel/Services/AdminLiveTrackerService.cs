using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Travora.Application.DTOs.Admin.LiveTracker;
using Travora.Application.Interfaces;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminLiveTrackerService : IAdminLiveTrackerService
{
    private readonly ApplicationDbContext _db;
    private readonly IUpstashRedisService _redis;

    public AdminLiveTrackerService(ApplicationDbContext db, IUpstashRedisService redis)
    {
        _db = db;
        _redis = redis;
    }

    public async Task<LiveEmployeeResponse> GetLastLocationsAsync(string? filter, string? search)
    {
        var drivers = await _db.Employees
            .Where(e => e.IsActive && e.JobRole == JobRole.Driver)
            .Select(e => new { e.EmployeeId, e.Firstname, e.Lastname, e.PhoneNumber })
            .ToListAsync();

        var response = new LiveEmployeeResponse();

        foreach (var driver in drivers)
        {
            var key = $"employee:{driver.EmployeeId}:last_location";
            var locationData = await _redis.GetAsync(key);
            
            bool isOnline = false;
            string status = "offline";
            decimal lat = 0, lng = 0;
            string lastUpdated = "offline";
            string? currentTask = null;
            string locationDesc = "Unknown";

            if (!string.IsNullOrEmpty(locationData))
            {
                // Parse JSON Redis value assuming { latitude, longitude, status, updatedAt, location }
                try
                {
                    using var doc = JsonDocument.Parse(locationData);
                    var root = doc.RootElement;
                    
                    isOnline = true;
                    if (root.TryGetProperty("status", out var statusProp)) status = statusProp.GetString() ?? "available";
                    if (root.TryGetProperty("latitude", out var latProp)) lat = latProp.GetDecimal();
                    if (root.TryGetProperty("longitude", out var lngProp)) lng = lngProp.GetDecimal();
                    if (root.TryGetProperty("location", out var locProp)) locationDesc = locProp.GetString() ?? "Unknown";
                    
                    if (root.TryGetProperty("updatedAt", out var updatedProp))
                    {
                        var updatedAt = updatedProp.GetDateTime();
                        var diff = DateTime.UtcNow - updatedAt;
                        lastUpdated = diff.TotalMinutes < 1 ? "Just now" : $"{(int)diff.TotalMinutes} mins ago";
                    }
                }
                catch { /* Ignore parsing errors */ }
            }

            if (status == "on_service" || status == "on_duty")
            {
                var currentOrder = await _db.OrderServices
                    .Include(os => os.PackageService)
                        .ThenInclude(ps => ps.Service)
                    .Include(os => os.Order)
                        .ThenInclude(o => o.PickupLocation)
                    .Where(os => os.AssignedEmployeeId == driver.EmployeeId && os.ServiceStatus == ServiceStatus.InProgress)
                    .FirstOrDefaultAsync();

                if (currentOrder != null)
                {
                    currentTask = $"{currentOrder.PackageService?.Service?.ServiceName ?? "Service"} - {currentOrder.Order?.PickupLocation?.City ?? "Unknown Location"}";
                }
            }

            var item = new LiveEmployeeItem
            {
                EmployeeId = driver.EmployeeId,
                Name = $"{driver.Firstname} {driver.Lastname}",
                Code = $"EMP{driver.EmployeeId:D3}",
                JobRole = "driver",
                Status = status,
                CurrentTask = currentTask,
                Location = locationDesc,
                Latitude = lat,
                Longitude = lng,
                IsOnline = isOnline,
                LastUpdated = lastUpdated,
                Mobile = driver.PhoneNumber
            };

            response.Employees.Add(item);
        }

        // Apply filters
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            response.Employees = response.Employees.Where(e => e.Name.ToLower().Contains(s) || e.Code.ToLower().Contains(s)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter) && filter != "all")
        {
            response.Employees = response.Employees.Where(e => e.Status == filter).ToList();
        }

        response.Available = response.Employees.Count(e => e.Status == "available");
        response.OnService = response.Employees.Count(e => e.Status == "on_service" || e.Status == "on_duty");

        return response;
    }

    public async Task<EmployeeLocationDetailResponse> GetEmployeeLocationDetailsAsync(int employeeId)
    {
        var driver = await _db.Employees
            .Where(e => e.EmployeeId == employeeId && e.JobRole == JobRole.Driver)
            .Select(e => new { e.EmployeeId, e.Firstname, e.Lastname })
            .FirstOrDefaultAsync() ?? throw new KeyNotFoundException("Driver not found");

        var key = $"employee:{driver.EmployeeId}:last_location";
        var locationData = await _redis.GetAsync(key);

        string status = "offline";
        decimal lat = 0, lng = 0;
        decimal? speedKmh = null, heading = null;
        bool isMoving = false;
        string lastUpdated = "offline";
        string? currentTask = null;

        if (!string.IsNullOrEmpty(locationData))
        {
            try
            {
                using var doc = JsonDocument.Parse(locationData);
                var root = doc.RootElement;
                    
                if (root.TryGetProperty("status", out var statusProp)) status = statusProp.GetString() ?? "available";
                if (root.TryGetProperty("latitude", out var latProp)) lat = latProp.GetDecimal();
                if (root.TryGetProperty("longitude", out var lngProp)) lng = lngProp.GetDecimal();
                if (root.TryGetProperty("speed", out var speedProp)) speedKmh = speedProp.GetDecimal();
                if (root.TryGetProperty("isMoving", out var movingProp)) isMoving = movingProp.GetBoolean();
                if (root.TryGetProperty("heading", out var headingProp)) heading = headingProp.GetDecimal();
                    
                if (root.TryGetProperty("updatedAt", out var updatedProp))
                {
                    var updatedAt = updatedProp.GetDateTime();
                    var diff = DateTime.UtcNow - updatedAt;
                    lastUpdated = diff.TotalMinutes < 1 ? "Just now" : $"{(int)diff.TotalMinutes} mins ago";
                }
            }
            catch { }
        }

        if (status == "on_service" || status == "on_duty")
        {
            var currentOrder = await _db.OrderServices
                .Include(os => os.PackageService)
                    .ThenInclude(ps => ps.Service)
                .Include(os => os.Order)
                    .ThenInclude(o => o.PickupLocation)
                .Where(os => os.AssignedEmployeeId == driver.EmployeeId && os.ServiceStatus == ServiceStatus.InProgress)
                .FirstOrDefaultAsync();

            if (currentOrder != null)
            {
                currentTask = $"Client pickup - {currentOrder.Order?.PickupLocation?.StreetAddress ?? "Unknown"}, {currentOrder.Order?.PickupLocation?.City ?? "Unknown City"}, {currentOrder.Order?.PickupLocation?.Country ?? "Unknown Country"}";
            }
        }

        return new EmployeeLocationDetailResponse
        {
            EmployeeId = driver.EmployeeId,
            Name = $"{driver.Firstname} {driver.Lastname}",
            Code = $"EMP{driver.EmployeeId:D3}",
            Status = status,
            CurrentTask = currentTask,
            Latitude = lat,
            Longitude = lng,
            SpeedKmh = speedKmh,
            IsMoving = isMoving,
            Heading = heading,
            LastUpdated = lastUpdated
        };
    }
}
