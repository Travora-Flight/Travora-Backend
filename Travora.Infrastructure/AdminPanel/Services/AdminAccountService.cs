using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Account;
using Travora.Application.Interfaces;
using Travora.Domain.Entities;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminAccountService : IAdminAccountService
{
    private readonly ApplicationDbContext _db;

    public AdminAccountService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AdminAccountResponse> GetAccountDetailsAsync(int adminId)
    {
        var admin = await _db.Admins.FindAsync(adminId)
            ?? throw new KeyNotFoundException("Admin not found");

        return new AdminAccountResponse
        {
            AdminId = admin.AdminId,
            FullName = admin.FullName,
            Email = admin.Email,
            Phone = admin.PhoneNumber ?? string.Empty,
            IsSuperAdmin = admin.IsSuperAdmin
        };
    }

    public async Task<AdminAccountResponse> UpdateAccountAsync(int adminId, UpdateAdminAccountRequest request)
    {
        var admin = await _db.Admins.FindAsync(adminId)
            ?? throw new KeyNotFoundException("Admin not found");

        // تحقق من الإيميل لو اتغير هل موجود لمستخدم تاني
        if (admin.Email != request.Email && await _db.Admins.AnyAsync(a => a.Email == request.Email))
            throw new InvalidOperationException("Email is already registered");

        admin.FullName = request.FullName;
        admin.Email = request.Email;
        admin.PhoneNumber = request.Phone;

        await _db.SaveChangesAsync();

        return new AdminAccountResponse
        {
            AdminId = admin.AdminId,
            FullName = admin.FullName,
            Email = admin.Email,
            Phone = admin.PhoneNumber ?? string.Empty,
            IsSuperAdmin = admin.IsSuperAdmin
        };
    }
}
