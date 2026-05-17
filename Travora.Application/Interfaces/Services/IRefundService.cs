using Travora.Application.DTOs.Refunds;

namespace Travora.Application.Interfaces.Services;

public interface IRefundService
{
    Task<RefundResponse> RequestRefundAsync(int customerId, int orderId, RefundRequest request);
    Task<RefundStatusResponse?> GetRefundStatusAsync(int customerId, int orderId);
    Task<List<AdminRefundListItem>> GetAllRefundsAsync();
    Task<RefundResponse> ApproveRefundAsync(int adminId, int refundId);
    Task<RefundResponse> RejectRefundAsync(int adminId, int refundId, AdminProcessRefundRequest request);
}
