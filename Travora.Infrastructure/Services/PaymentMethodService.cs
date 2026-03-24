using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Payments;
using Travora.Application.Interfaces.Services;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly ApplicationDbContext _db;

    public PaymentMethodService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PaymentMethodsResponse> GetCustomerPaymentMethodsAsync(int customerId)
    {
        var methods = await _db.PaymentMethods
            .Where(pm => pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted)
            .OrderByDescending(pm => pm.IsDefault)
            .ThenBy(pm => pm.AddedAt)
            .Select(pm => new PaymentMethodDto
            {
                PaymentMethodId = pm.PaymentMethodId,
                CardLastFour = pm.CardLastFour,
                CardBrand = pm.CardBrand,
                CardExpiryMonth = pm.CardExpiryMonth,
                CardExpiryYear = pm.CardExpiryYear,
                PaymentFunding = pm.PaymentFunding.ToString(),
                IsDefault = pm.IsDefault
            })
            .ToListAsync();

        return new PaymentMethodsResponse { PaymentMethods = methods };
    }

    public async Task<bool> SetDefaultPaymentMethodAsync(int customerId, int paymentMethodId)
    {
        var target = await _db.PaymentMethods
            .FirstOrDefaultAsync(pm => pm.PaymentMethodId == paymentMethodId
                && pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted);

        if (target == null)
            return false;

        // Remove IsDefault from all others
        var allMethods = await _db.PaymentMethods
            .Where(pm => pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted)
            .ToListAsync();

        foreach (var pm in allMethods)
        {
            pm.IsDefault = pm.PaymentMethodId == paymentMethodId;
            pm.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(bool Success, string Message)> DeletePaymentMethodAsync(int customerId, int paymentMethodId)
    {
        var target = await _db.PaymentMethods
            .FirstOrDefaultAsync(pm => pm.PaymentMethodId == paymentMethodId
                && pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted);

        if (target == null)
            return (false, "الكارت مش موجود");

        // Check if it's the only active card
        var activeCount = await _db.PaymentMethods
            .CountAsync(pm => pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted);

        if (activeCount <= 1)
            return (false, "لا يمكن حذف الكارت الوحيد");

        var now = DateTime.UtcNow;
        target.IsDeleted = true;
        target.IsActive = false;
        target.UpdatedAt = now;

        // If deleted card was default → make oldest remaining card default
        if (target.IsDefault)
        {
            target.IsDefault = false;
            var oldest = await _db.PaymentMethods
                .Where(pm => pm.CustomerId == customerId && pm.IsActive && !pm.IsDeleted && pm.PaymentMethodId != paymentMethodId)
                .OrderBy(pm => pm.AddedAt)
                .FirstOrDefaultAsync();

            if (oldest != null)
            {
                oldest.IsDefault = true;
                oldest.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync();
        return (true, "تم حذف الكارت بنجاح");
    }
}
