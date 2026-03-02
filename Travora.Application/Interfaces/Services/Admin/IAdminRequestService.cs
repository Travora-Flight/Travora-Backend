using Travora.Application.DTOs.Admin.Requests;

namespace Travora.Application.Interfaces;

public interface IAdminRequestService
{
    Task<RequestPagedResponse> GetRequestsAsync(string? search, string? filter, string? status, int page, int pageSize);
    Task<RequestDetailResponse> GetRequestDetailsAsync(int orderId);
    Task<bool> AssignEmployeeAsync(int orderId, AssignEmployeeRequest request);
}
