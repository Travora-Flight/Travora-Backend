using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Vehicles;
using Travora.Application.Interfaces.Services;
using Travora.Domain.Entities;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminVehicleService : IAdminVehicleService
{
    private readonly ApplicationDbContext _db;

    public AdminVehicleService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<VehicleResponse>> GetAllVehiclesAsync()
    {
        return await _db.Vehicles
            .Select(v => new VehicleResponse
            {
                VehicleId = v.VehicleId,
                PlateNumber = v.PlateNumber,
                Brand = v.Brand,
                Model = v.Model,
                Year = v.Year,
                Color = v.Color,
                Capacity = v.Capacity,
                IsActive = v.IsActive,
                IsAssigned = v.Employees.Any(e => e.IsActive && !e.IsDeleted),
                Employees = null // Exclude heavy employee list in basic overview
            })
            .ToListAsync();
    }

    public async Task<VehicleResponse> GetVehicleByIdAsync(int id)
    {
        var v = await _db.Vehicles
            .Include(v => v.Employees)
            .FirstOrDefaultAsync(x => x.VehicleId == id)
            ?? throw new KeyNotFoundException("Vehicle not found");

        var activeEmployee = v.Employees.FirstOrDefault(e => e.IsActive && !e.IsDeleted);

        return new VehicleResponse
        {
            VehicleId = v.VehicleId,
            PlateNumber = v.PlateNumber,
            Brand = v.Brand,
            Model = v.Model,
            Year = v.Year,
            Color = v.Color,
            Capacity = v.Capacity,
            IsActive = v.IsActive,
            IsAssigned = activeEmployee != null,
            Employees = v.Employees.Select(e => new VehicleEmployeeResponse
            {
                EmployeeId = e.EmployeeId,
                Name = $"{e.Firstname} {e.Lastname}",
                Mobile = e.PhoneNumber,
                Status = e.IsActive && !e.IsDeleted ? "Active" : "Inactive",
                Email = e.Email,
                ShiftType = e.ShiftType.ToString()
            }).ToList()
        };
    }

    public async Task<VehicleResponse> CreateVehicleAsync(CreateVehicleRequest request)
    {
        if (await _db.Vehicles.AnyAsync(v => v.PlateNumber == request.PlateNumber))
            throw new InvalidOperationException("Plate number already exists");

        var vehicle = new Vehicle
        {
            PlateNumber = request.PlateNumber,
            Brand = request.Brand,
            Model = request.Model,
            Year = request.Year,
            Color = request.Color,
            Capacity = request.Capacity,
            IsActive = request.IsActive,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync();

        return await GetVehicleByIdAsync(vehicle.VehicleId);
    }

    public async Task<VehicleResponse> UpdateVehicleAsync(int id, UpdateVehicleRequest request)
    {
        var vehicle = await _db.Vehicles.FindAsync(id)
            ?? throw new KeyNotFoundException("Vehicle not found");

        if (!string.IsNullOrEmpty(request.PlateNumber) && request.PlateNumber != vehicle.PlateNumber)
        {
            if (await _db.Vehicles.AnyAsync(v => v.PlateNumber == request.PlateNumber))
                throw new InvalidOperationException("Plate number already exists");
            vehicle.PlateNumber = request.PlateNumber;
        }

        if (!string.IsNullOrEmpty(request.Brand)) vehicle.Brand = request.Brand;
        if (!string.IsNullOrEmpty(request.Model)) vehicle.Model = request.Model;
        if (request.Year.HasValue) vehicle.Year = request.Year.Value;
        if (!string.IsNullOrEmpty(request.Color)) vehicle.Color = request.Color;
        if (request.Capacity.HasValue) vehicle.Capacity = request.Capacity.Value;
        if (request.IsActive.HasValue) vehicle.IsActive = request.IsActive.Value;

        vehicle.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        
        return await GetVehicleByIdAsync(vehicle.VehicleId);
    }

    public async Task<bool> DeleteVehicleAsync(int id)
    {
        var vehicle = await _db.Vehicles
            .Include(v => v.Employees)
            .FirstOrDefaultAsync(x => x.VehicleId == id)
            ?? throw new KeyNotFoundException("Vehicle not found");

        if (vehicle.Employees.Any(e => e.IsActive && !e.IsDeleted))
        {
            throw new InvalidOperationException("Cannot delete a vehicle that is currently assigned to an active employee");
        }

        // Perform Soft Delete
        vehicle.IsDeleted = true;
        vehicle.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateVehicleStatusAsync(int id, VehicleStatusRequest request)
    {
        var vehicle = await _db.Vehicles.FindAsync(id)
            ?? throw new KeyNotFoundException("Vehicle not found");

        vehicle.IsActive = request.IsActive;
        vehicle.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }
}
