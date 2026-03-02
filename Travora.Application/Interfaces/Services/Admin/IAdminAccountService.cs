using Travora.Application.DTOs.Admin.Account;

namespace Travora.Application.Interfaces;

public interface IAdminAccountService
{
    Task<AdminAccountResponse> GetAccountDetailsAsync(int adminId);
    Task<AdminAccountResponse> UpdateAccountAsync(int adminId, UpdateAdminAccountRequest request);
}
