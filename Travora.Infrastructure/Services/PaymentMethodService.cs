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
            .Where(pm => pm.CustomerId == customerId
                && pm.IsActive && !pm.IsDeleted
                && pm.PaymobCardToken != null        // Must have a valid token to be usable
                && pm.CardLastFour != "0000")         // Exclude legacy placeholder records
            .OrderByDescending(pm => pm.IsDefault)
            .ThenBy(pm => pm.AddedAt)
            .Select(pm => new PaymentMethodDto
            {
                PaymentMethodId = pm.PaymentMethodId,
                CardHolderName = pm.CardHolderName,
                CardLastFour = pm.CardLastFour,
                CardBrand = pm.CardBrand,
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
            return (false, "Card not found");

        // Payment is captured upfront at booking time.
        // The card is no longer needed after payment is collected.
        // Refunds (full or partial) are processed via TransactionId stored in the Payment table.
        // Therefore, we allow deletion in all order statuses with no restrictions.

        var now = DateTime.UtcNow;
        target.IsDeleted = true;
        target.IsActive = false;
        target.UpdatedAt = now;

        // If deleted card was default → promote the oldest remaining active card to default
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
        return (true, "Card deleted successfully");
    }
}
