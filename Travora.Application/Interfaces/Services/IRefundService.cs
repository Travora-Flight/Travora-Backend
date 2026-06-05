using Travora.Application.DTOs.Refunds;

namespace Travora.Application.Interfaces.Services;

public interface IRefundService
{
    Task<RefundResponse> RequestRefundAsync(int customerId, int orderId, RefundRequest request);
    Task<RefundStatusResponse?> GetRefundStatusAsync(int customerId, int orderId);
    Task<List<AdminRefundListItem>> GetAllRefundsAsync();
    Task<RefundResponse> ApproveRefundAsync(int adminId, int refundId);
    Task<RefundResponse> RejectRefundAsync(int adminId, int refundId, AdminProcessRefundRequest request);

    /// <summary>
    /// Processes a partial refund initiated by an employee (e.g. customs fee refund).
    /// Directly calls Paymob — no admin approval needed.
    /// </summary>
    Task<RefundResponse> ProcessEmployeeRefundAsync(int orderId, decimal amount, string reason);
}
