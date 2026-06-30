using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Settings;
using Travora.Application.Interfaces;
using Travora.Domain.Entities;
using Travora.Infrastructure.Data;
using BCrypt.Net;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminSettingsService : IAdminSettingsService
{
    private readonly ApplicationDbContext _db;

    public AdminSettingsService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AppSettingsResponse> GetSettingsAsync()
    {
        var settings = await _db.AppSettings.FirstOrDefaultAsync();

        if (settings == null)
            return new AppSettingsResponse();

        return new AppSettingsResponse
        {
            General = new GeneralSettingsDto
            {
                CompanyName = settings.CompanyName,
                Email = settings.CompanyEmail,
                Phone = settings.CompanyPhone,
                Address = settings.CompanyAddress,
                Timezone = settings.Timezone,
                Language = settings.Language
            },
            Tracking = new TrackingSettingsDto
            {
                ShowEmployeeNamesOnMap = settings.ShowEmployeeNamesOnMap,
                AutoRefresh = settings.AutoRefresh
            }
        };
    }

    public async Task<AppSettingsResponse> UpdateGeneralSettingsAsync(UpdateGeneralSettingsRequest request)
    {
        var settings = await _db.AppSettings.FirstOrDefaultAsync() 
            ?? new Domain.Entities.AppSettings();

        if (request.CompanyName != null) settings.CompanyName    = request.CompanyName;
        if (request.Email       != null) settings.CompanyEmail   = request.Email;
        if (request.Phone       != null) settings.CompanyPhone   = request.Phone;
        if (request.Address     != null) settings.CompanyAddress = request.Address;
        if (request.Timezone    != null) settings.Timezone       = request.Timezone;
        if (request.Language    != null) settings.Language       = request.Language;

        if (settings.SettingsId == 0)
            _db.AppSettings.Add(settings);

        await _db.SaveChangesAsync();

        return await GetSettingsAsync();
    }

    public async Task<AppSettingsResponse> UpdateTrackingSettingsAsync(UpdateTrackingSettingsRequest request)
    {
        var settings = await _db.AppSettings.FirstOrDefaultAsync() 
            ?? new Domain.Entities.AppSettings();

        if (request.ShowEmployeeNamesOnMap.HasValue) settings.ShowEmployeeNamesOnMap = request.ShowEmployeeNamesOnMap.Value;
        if (request.AutoRefresh.HasValue)            settings.AutoRefresh            = request.AutoRefresh.Value;

        if (settings.SettingsId == 0)
            _db.AppSettings.Add(settings);

        await _db.SaveChangesAsync();

        return await GetSettingsAsync();
    }

    public async Task<bool> ChangePasswordAsync(int adminId, ChangePasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmPassword)
            throw new InvalidOperationException("New passwords do not match");

        if (request.CurrentPassword == request.NewPassword)
            throw new InvalidOperationException("New password cannot be the same as the current password");

        var admin = await _db.Admins.FindAsync(adminId)
            ?? throw new KeyNotFoundException("Admin not found");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, admin.PasswordHash))
            throw new InvalidOperationException("Invalid current password");

        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, admin.PasswordHash))
            throw new InvalidOperationException("New password cannot be the same as the current password");

        admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();

        return true;
    }
}
