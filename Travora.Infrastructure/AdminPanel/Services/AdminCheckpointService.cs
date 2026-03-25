using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Checkpoints;
using Travora.Application.Interfaces.Services;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminCheckpointService : IAdminCheckpointService
{
    private readonly ApplicationDbContext _db;

    public AdminCheckpointService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<CheckpointResponse>> GetAllCheckpointsAsync()
    {
        return await _db.Checkpoints
            .Select(c => new CheckpointResponse
            {
                CheckpointId = c.CheckpointId,
                CheckpointName = c.CheckpointName,
                CheckpointType = c.CheckpointType.ToString(),
                SequenceOrder = c.SequenceOrder,
                GpsLatitude = c.GpsLatitude,
                GpsLongitude = c.GpsLongitude,
                IsAssigned = c.Employees.Any(e => e.IsActive && !e.IsDeleted),
                AssignedToEmployeeId = c.Employees.Where(e => e.IsActive && !e.IsDeleted).Select(e => (int?)e.EmployeeId).FirstOrDefault(),
                AssignedToEmployeeName = c.Employees.Where(e => e.IsActive && !e.IsDeleted).Select(e => e.Firstname + " " + e.Lastname).FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<CheckpointResponse> GetCheckpointByIdAsync(int id)
    {
        var c = await _db.Checkpoints
            .Include(x => x.Employees)
            .FirstOrDefaultAsync(x => x.CheckpointId == id)
            ?? throw new KeyNotFoundException("نقطة التفتيش غير موجودة");

        var activeEmployee = c.Employees.FirstOrDefault(e => e.IsActive && !e.IsDeleted);

        return new CheckpointResponse
        {
            CheckpointId = c.CheckpointId,
            CheckpointName = c.CheckpointName,
            CheckpointType = c.CheckpointType.ToString(),
            SequenceOrder = c.SequenceOrder,
            GpsLatitude = c.GpsLatitude,
            GpsLongitude = c.GpsLongitude,
            IsAssigned = activeEmployee != null,
            AssignedToEmployeeId = activeEmployee?.EmployeeId,
            AssignedToEmployeeName = activeEmployee != null ? $"{activeEmployee.Firstname} {activeEmployee.Lastname}" : null
        };
    }

    public async Task<CheckpointResponse> CreateCheckpointAsync(CreateCheckpointRequest request)
    {
        if (!Enum.TryParse<CheckpointType>(request.CheckpointType, true, out var cType))
        {
            throw new ArgumentException("نوع نقطة التفتيش غير صالح");
        }

        var checkpoint = new Checkpoint
        {
            CheckpointName = request.CheckpointName,
            CheckpointType = cType,
            Description = request.Description ?? string.Empty,
            SequenceOrder = request.SequenceOrder,
            GpsLatitude = request.GpsLatitude,
            GpsLongitude = request.GpsLongitude,
            AirportId = request.AirportId
        };

        _db.Checkpoints.Add(checkpoint);
        await _db.SaveChangesAsync();

        return await GetCheckpointByIdAsync(checkpoint.CheckpointId);
    }

    public async Task<CheckpointResponse> UpdateCheckpointAsync(int id, UpdateCheckpointRequest request)
    {
        var c = await _db.Checkpoints.FindAsync(id)
            ?? throw new KeyNotFoundException("نقطة التفتيش غير موجودة");

        if (!string.IsNullOrEmpty(request.CheckpointName)) c.CheckpointName = request.CheckpointName;
        if (!string.IsNullOrEmpty(request.Description)) c.Description = request.Description;
        if (request.SequenceOrder.HasValue) c.SequenceOrder = request.SequenceOrder.Value;
        if (request.GpsLatitude.HasValue) c.GpsLatitude = request.GpsLatitude.Value;
        if (request.GpsLongitude.HasValue) c.GpsLongitude = request.GpsLongitude.Value;
        if (request.AirportId.HasValue) c.AirportId = request.AirportId.Value;

        if (!string.IsNullOrEmpty(request.CheckpointType))
        {
            if (!Enum.TryParse<CheckpointType>(request.CheckpointType, true, out var cType))
            {
                throw new ArgumentException("نوع نقطة التفتيش غير صالح");
            }
            c.CheckpointType = cType;
        }

        await _db.SaveChangesAsync();
        return await GetCheckpointByIdAsync(c.CheckpointId);
    }

    public async Task<bool> DeleteCheckpointAsync(int id)
    {
        var c = await _db.Checkpoints
            .Include(x => x.Employees)
            .FirstOrDefaultAsync(x => x.CheckpointId == id)
            ?? throw new KeyNotFoundException("نقطة التفتيش غير موجودة");

        if (c.Employees.Any(e => e.IsActive && !e.IsDeleted))
        {
            throw new InvalidOperationException("لا يمكن حذف نقطة تفتيش معينة لموظف");
        }

        _db.Checkpoints.Remove(c);
        await _db.SaveChangesAsync();
        return true;
    }
}
