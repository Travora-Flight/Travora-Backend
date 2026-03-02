using Travora.Application.DTOs.Admin.Passport;

namespace Travora.Application.Interfaces;

public interface IAdminPassportService
{
    Task<PassportVerificationListResponse> GetPassportVerificationsAsync(string? status);
    Task<bool> ApprovePassportAsync(int documentId, int adminId);
    Task<bool> RejectPassportAsync(int documentId, int adminId, RejectPassportRequest request);
}
