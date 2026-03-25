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
                IsAssigned = v.Employees.Any(e => e.IsActive && !e.IsDeleted),
                AssignedToEmployeeId = v.Employees.Where(e => e.IsActive && !e.IsDeleted).Select(e => (int?)e.EmployeeId).FirstOrDefault(),
                AssignedToEmployeeName = v.Employees.Where(e => e.IsActive && !e.IsDeleted).Select(e => e.Firstname + " " + e.Lastname).FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<VehicleResponse> GetVehicleByIdAsync(int id)
    {
        var v = await _db.Vehicles
            .Include(v => v.Employees)
            .FirstOrDefaultAsync(x => x.VehicleId == id)
            ?? throw new KeyNotFoundException("المركبة غير موجودة");

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
            IsAssigned = activeEmployee != null,
            AssignedToEmployeeId = activeEmployee?.EmployeeId,
            AssignedToEmployeeName = activeEmployee != null ? $"{activeEmployee.Firstname} {activeEmployee.Lastname}" : null
        };
    }

    public async Task<VehicleResponse> CreateVehicleAsync(CreateVehicleRequest request)
    {
        if (await _db.Vehicles.AnyAsync(v => v.PlateNumber == request.PlateNumber))
            throw new InvalidOperationException("رقم اللوحة موجود مسبقا");

        var vehicle = new Vehicle
        {
            PlateNumber = request.PlateNumber,
            Brand = request.Brand,
            Model = request.Model,
            Year = request.Year,
            Color = request.Color,
            Capacity = request.Capacity
        };

        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync();

        return await GetVehicleByIdAsync(vehicle.VehicleId);
    }

    public async Task<VehicleResponse> UpdateVehicleAsync(int id, UpdateVehicleRequest request)
    {
        var vehicle = await _db.Vehicles.FindAsync(id)
            ?? throw new KeyNotFoundException("المركبة غير موجودة");

        if (!string.IsNullOrEmpty(request.PlateNumber) && request.PlateNumber != vehicle.PlateNumber)
        {
            if (await _db.Vehicles.AnyAsync(v => v.PlateNumber == request.PlateNumber))
                throw new InvalidOperationException("رقم اللوحة موجود مسبقا");
            vehicle.PlateNumber = request.PlateNumber;
        }

        if (!string.IsNullOrEmpty(request.Brand)) vehicle.Brand = request.Brand;
        if (!string.IsNullOrEmpty(request.Model)) vehicle.Model = request.Model;
        if (request.Year.HasValue) vehicle.Year = request.Year.Value;
        if (!string.IsNullOrEmpty(request.Color)) vehicle.Color = request.Color;
        if (request.Capacity.HasValue) vehicle.Capacity = request.Capacity.Value;

        await _db.SaveChangesAsync();
        return await GetVehicleByIdAsync(vehicle.VehicleId);
    }

    public async Task<bool> DeleteVehicleAsync(int id)
    {
        var vehicle = await _db.Vehicles
            .Include(v => v.Employees)
            .FirstOrDefaultAsync(x => x.VehicleId == id)
            ?? throw new KeyNotFoundException("المركبة غير موجودة");

        if (vehicle.Employees.Any(e => e.IsActive && !e.IsDeleted))
        {
            throw new InvalidOperationException("لا يمكن حذف مركبة معينة لموظف");
        }

        _db.Vehicles.Remove(vehicle);
        await _db.SaveChangesAsync();
        return true;
    }
}
