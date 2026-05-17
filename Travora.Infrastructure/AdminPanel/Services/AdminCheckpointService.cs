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
                GpsLatitude = c.GpsLatitude,
                GpsLongitude = c.GpsLongitude,
                IsAssigned = c.Employees.Any(e => e.IsActive && !e.IsDeleted)
            })
            .ToListAsync();
    }

    public async Task<IEnumerable<CheckpointEmployeeResponse>> GetCheckpointEmployeesAsync(int checkpointId)
    {
        var checkpoint = await _db.Checkpoints
            .Include(c => c.Employees)
            .FirstOrDefaultAsync(c => c.CheckpointId == checkpointId)
            ?? throw new KeyNotFoundException("Checkpoint not found");

        var activeEmployees = checkpoint.Employees
            .Where(e => !e.IsDeleted)
            .ToList();

        if (!activeEmployees.Any())
        {
            throw new InvalidOperationException($"No employees are currently assigned to the checkpoint: {checkpoint.CheckpointName}");
        }

        return activeEmployees.Select(e => new CheckpointEmployeeResponse
        {
            EmployeeId = e.EmployeeId,
            Firstname = e.Firstname,
            Lastname = e.Lastname,
            Email = e.Email,
            PhoneNumber = e.PhoneNumber,
            ProfileImagePath = e.ProfileImagePath,
            ShiftType = e.ShiftType.ToString(),
            IsActive = e.IsActive
        }).ToList();
    }

    public async Task<CheckpointResponse> CreateCheckpointAsync(CreateCheckpointRequest request)
    {
        if (!Enum.TryParse<CheckpointType>(request.CheckpointType, true, out var cType))
        {
            throw new ArgumentException("Invalid checkpoint type");
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

        return new CheckpointResponse
        {
            CheckpointId = checkpoint.CheckpointId,
            CheckpointName = checkpoint.CheckpointName,
            GpsLatitude = checkpoint.GpsLatitude,
            GpsLongitude = checkpoint.GpsLongitude,
            IsAssigned = false
        };
    }

    public async Task<CheckpointResponse> UpdateCheckpointAsync(int id, UpdateCheckpointRequest request)
    {
        var c = await _db.Checkpoints.FindAsync(id)
            ?? throw new KeyNotFoundException("Checkpoint not found");

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
                throw new ArgumentException("Invalid checkpoint type");
            }
            c.CheckpointType = cType;
        }

        await _db.SaveChangesAsync();

        return new CheckpointResponse
        {
            CheckpointId = c.CheckpointId,
            CheckpointName = c.CheckpointName,
            GpsLatitude = c.GpsLatitude,
            GpsLongitude = c.GpsLongitude,
            IsAssigned = await _db.Employees.AnyAsync(e => e.CheckpointId == c.CheckpointId && e.IsActive && !e.IsDeleted)
        };
    }

    public async Task<bool> DeleteCheckpointAsync(int id)
    {
        var c = await _db.Checkpoints
            .Include(x => x.Employees)
            .FirstOrDefaultAsync(x => x.CheckpointId == id)
            ?? throw new KeyNotFoundException("Checkpoint not found");

        if (c.Employees.Any(e => e.IsActive && !e.IsDeleted))
        {
            throw new InvalidOperationException("Cannot delete a checkpoint that is assigned to an employee");
        }

        _db.Checkpoints.Remove(c);
        await _db.SaveChangesAsync();
        return true;
    }
}
