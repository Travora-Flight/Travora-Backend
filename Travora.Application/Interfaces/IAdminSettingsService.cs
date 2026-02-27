using Travora.Application.DTOs.Admin.Settings;

namespace Travora.Application.Interfaces;

public interface IAdminSettingsService
{
    Task<AppSettingsResponse> GetSettingsAsync();
    Task<AppSettingsResponse> UpdateGeneralSettingsAsync(UpdateGeneralSettingsRequest request);
    Task<AppSettingsResponse> UpdateTrackingSettingsAsync(UpdateTrackingSettingsRequest request);
    Task<bool> ChangePasswordAsync(int adminId, ChangePasswordRequest request);
}
