using Travora.Domain.Enums;
using Travora.Application.DTOs.Admin.Passport;

namespace Travora.Application.Interfaces;

public interface IAdminPassportService
{
    Task<PassportVerificationListResponse> GetPassportVerificationsAsync(PassportVerificationStatusFilter status = PassportVerificationStatusFilter.Pending, int pageNumber = 1, int pageSize = 10, string? searchTerm = null);
    Task<PassportVerificationCountsResponse> GetPassportVerificationCountsAsync();
    Task<PassportVerificationDetailsResponse?> GetPassportVerificationDetailsAsync(int documentId);
    Task<bool> ApprovePassportAsync(int documentId, int adminId, ApprovePassportRequest request);
    Task<bool> RejectPassportAsync(int documentId, int adminId, RejectPassportRequest request);
}
