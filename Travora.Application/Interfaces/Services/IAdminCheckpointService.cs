using Travora.Application.DTOs.Admin.Checkpoints;

namespace Travora.Application.Interfaces.Services;

public interface IAdminCheckpointService
{
    Task<IEnumerable<CheckpointResponse>> GetAllCheckpointsAsync();
    Task<IEnumerable<CheckpointEmployeeResponse>> GetCheckpointEmployeesAsync(int checkpointId);
    Task<CheckpointResponse> CreateCheckpointAsync(CreateCheckpointRequest request);
    Task<CheckpointResponse> UpdateCheckpointAsync(int id, UpdateCheckpointRequest request);
    Task<bool> DeleteCheckpointAsync(int id);
}
