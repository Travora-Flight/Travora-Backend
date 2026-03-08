using Microsoft.EntityFrameworkCore;
using Travora.Application.DTOs.Admin.Passport;
using Travora.Application.Interfaces;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Application.Interfaces.External.Communication;
using Travora.Infrastructure.Data;

namespace Travora.Infrastructure.AdminPanel.Services;

public class AdminPassportService : IAdminPassportService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;

    public AdminPassportService(ApplicationDbContext db, IEmailService emailService)
    {
        _db = db;
        _emailService = emailService;
    }

    public async Task<PassportVerificationListResponse> GetPassportVerificationsAsync(string? status)
    {
        var query = from d in _db.Documents
                    join c in _db.Customers on d.OwnerId equals c.CustomerId
                    join p in _db.PassportValidations on d.DocumentId equals p.DocumentId
                    where d.OwnerType == DocumentOwnerType.Customer && d.DocumentType == DocumentType.Passport
                       && p.ManualReviewRequired == true
                    select new { d, c, p };

        var allItems = await query.ToListAsync();

        var pendingCount = allItems.Count(x => x.d.VerificationStatus == VerificationStatus.Pending || x.d.VerificationStatus == VerificationStatus.UnderReview);
        var approvedCount = allItems.Count(x => x.d.VerificationStatus == VerificationStatus.Approved);
        var rejectedCount = allItems.Count(x => x.d.VerificationStatus == VerificationStatus.Rejected);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status.Equals("pending", StringComparison.OrdinalIgnoreCase))
                allItems = allItems.Where(x => x.d.VerificationStatus == VerificationStatus.Pending || x.d.VerificationStatus == VerificationStatus.UnderReview).ToList();
            else if (status.Equals("approved", StringComparison.OrdinalIgnoreCase))
                allItems = allItems.Where(x => x.d.VerificationStatus == VerificationStatus.Approved).ToList();
            else if (status.Equals("rejected", StringComparison.OrdinalIgnoreCase))
                allItems = allItems.Where(x => x.d.VerificationStatus == VerificationStatus.Rejected).ToList();
        }

        var passports = allItems.Select(x => new PassportVerificationItem
        {
            DocumentId = x.d.DocumentId,
            CustomerName = $"{x.c.Firstname} {x.c.Lastname}",
            RequestNumber = x.d.DocumentId.ToString(),
            RequestDate = x.d.CreatedAt.ToString("dd/MM/yyyy"),
            Mobile = x.c.PhoneNumber,
            Email = x.c.Email,
            PassportImageUrl = x.d.FilePath,
            PassportInfo = new PassportInfoDetails
            {
                PassportNumber = x.c.PassportNumber,
                Nationality = x.c.Nationality,
                DateOfBirth = x.c.DateOfBirth.ToString("dd/MM/yyyy"),
                ExpiryDate = x.c.PassportExpiryDate.ToString("dd/MM/yyyy")
                // Issue Date might not be in Customer entity, omitted if not available
            },
            Status = x.d.VerificationStatus.ToString().ToLower(),
            OcrConfidenceScore = Math.Round(x.p.OcrConfidenceScore, 2),
            ManualReviewRequired = x.p.ManualReviewRequired
        }).ToList();

        return new PassportVerificationListResponse
        {
            PendingCount = pendingCount,
            ApprovedCount = approvedCount,
            RejectedCount = rejectedCount,
            Passports = passports
        };
    }

    public async Task<bool> ApprovePassportAsync(int documentId, int adminId, ApprovePassportRequest request)
    {
        var document = await _db.Documents.FindAsync(documentId) 
            ?? throw new KeyNotFoundException("Document not found");
        var validation = await _db.PassportValidations.FirstOrDefaultAsync(v => v.DocumentId == documentId)
            ?? throw new KeyNotFoundException("Passport validation not found");
        var customer = await _db.Customers.FindAsync(document.OwnerId)
            ?? throw new KeyNotFoundException("Customer not found");

        bool passportExists = await _db.Customers.AnyAsync(c => c.PassportNumber == request.PassportNumber && c.CustomerId != customer.CustomerId) ||
                              await _db.Companions.AnyAsync(c => c.PassportNumber == request.PassportNumber);

        if (passportExists)
            throw new InvalidOperationException("رقم جواز السفر المدخل مسجل بالفعل لشخص آخر في النظام.");

        DateTime.TryParse(request.DateOfBirth, out var parsedDob);
        DateTime.TryParse(request.ExpiryDate, out var parsedExpiry);

        document.VerificationStatus = VerificationStatus.Approved;
        document.VerifiedByAdminId = adminId;
        document.VerifiedAt = DateTime.UtcNow;

        validation.ValidationStatus = PassportValidationStatus.Passed;
        
        customer.PassportNumber = request.PassportNumber;
        customer.Nationality = request.Nationality;
        customer.DateOfBirth = parsedDob != default ? parsedDob : customer.DateOfBirth;
        customer.PassportExpiryDate = parsedExpiry != default ? parsedExpiry : customer.PassportExpiryDate;
        customer.Gender = request.Gender; // Update Gender from Admin Note
        
        customer.AccountStatus = CustomerAccountStatus.Verified;
        customer.EmailVerified = true; // Activating Email automatically upon Admin Approval

        var notification = new Notification
        {
            UserId = customer.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.AccountAlert, // Assuming enum value
            Title = "Passport Verified",
            Message = "تم التحقق من جواز السفر بنجاح وأصبح حسابك مفعل.",
            IsRead = false,
            SentAt = DateTime.UtcNow
        };
        _db.Notifications.Add(notification);

        await _db.SaveChangesAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.SendEmailAsync(
                    customer.Email,
                    "مرحباً بك في Travora - تم تفعيل الحساب",
                    $"<h2>أهلاً {customer.Firstname} 👋</h2><p>تم الحصول على بياناتك ورقم جوازك (<b style='letter-spacing:1px;'>{customer.PassportNumber}</b>) ومراجعة جواز سفرك يدوياً بنجاح.</p><p>حسابك الآن <b>مفعل</b> ويمكنك البدء في حجز وتتبع رحلاتك.</p>");
            }
            catch { /* Ignore */ }
        });

        return true;
    }

    public async Task<bool> RejectPassportAsync(int documentId, int adminId, RejectPassportRequest request)
    {
        var document = await _db.Documents.FindAsync(documentId) 
            ?? throw new KeyNotFoundException("Document not found");
        var validation = await _db.PassportValidations.FirstOrDefaultAsync(v => v.DocumentId == documentId)
            ?? throw new KeyNotFoundException("Passport validation not found");
        var customer = await _db.Customers.FindAsync(document.OwnerId)
            ?? throw new KeyNotFoundException("Customer not found");

        document.VerificationStatus = VerificationStatus.Rejected;
        document.VerifiedByAdminId = adminId;
        document.VerifiedAt = DateTime.UtcNow;

        validation.ValidationStatus = PassportValidationStatus.Failed;
        
        customer.AccountStatus = CustomerAccountStatus.Suspended; // Or Rejected

        var notification = new Notification
        {
            UserId = customer.CustomerId,
            UserType = UserType.Customer,
            NotificationType = NotificationType.AccountAlert,
            Title = "Passport Rejected",
            Message = $"تم رفض جواز السفر. السبب: {request.Reason}",
            IsRead = false,
            SentAt = DateTime.UtcNow
        };
        _db.Notifications.Add(notification);

        await _db.SaveChangesAsync();
        return true;
    }
}
