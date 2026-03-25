using Travora.Application.DTOs.Admin.Vehicles;

namespace Travora.Application.Interfaces.Services;

public interface IAdminVehicleService
{
    Task<IEnumerable<VehicleResponse>> GetAllVehiclesAsync();
    Task<VehicleResponse> GetVehicleByIdAsync(int id);
    Task<VehicleResponse> CreateVehicleAsync(CreateVehicleRequest request);
    Task<VehicleResponse> UpdateVehicleAsync(int id, UpdateVehicleRequest request);
    Task<bool> DeleteVehicleAsync(int id);
}
