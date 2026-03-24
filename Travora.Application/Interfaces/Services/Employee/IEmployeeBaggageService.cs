using Microsoft.AspNetCore.Http;
using Travora.Application.DTOs.Employee.Baggage;

namespace Travora.Application.Interfaces.Services.Employee;

public interface IEmployeeBaggageService
{
    Task<BaggageScanResponse> ScanBaggageAsync(int employeeId, BaggageScanRequest request);
    Task<BaggagePhotoResponse> UploadBaggagePhotosAsync(int employeeId, int baggageId, List<IFormFile> photos);
    Task<CheckpointUpdateResponse> UpdateCheckpointAsync(int employeeId, CheckpointUpdateRequest request);
    Task<LockBaggageResponse> AssignLockCodeAsync(int employeeId, int baggageId, LockBaggageRequest request);
}
