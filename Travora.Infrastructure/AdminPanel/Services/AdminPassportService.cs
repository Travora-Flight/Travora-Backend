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

    public async Task<PassportVerificationListResponse> GetPassportVerificationsAsync(
        PassportVerificationStatusFilter status = PassportVerificationStatusFilter.Pending, int pageNumber = 1, int pageSize = 10, string? searchTerm = null)
    {
        var query = from d in _db.Documents
                    join c in _db.Customers on d.OwnerId equals c.CustomerId
                    join p in _db.PassportValidations on d.DocumentId equals p.DocumentId
                    where d.OwnerType == DocumentOwnerType.Customer && d.DocumentType == DocumentType.Passport
                       && p.ManualReviewRequired == true
                    select new { d, c, p };

        // 1. Apply status filter
        if (status != PassportVerificationStatusFilter.All)
        {
            if (status == PassportVerificationStatusFilter.Pending)
            {
                query = query.Where(x => x.d.VerificationStatus == VerificationStatus.Pending || x.d.VerificationStatus == VerificationStatus.UnderReview);
            }
            else if (status == PassportVerificationStatusFilter.Approved)
            {
                query = query.Where(x => x.d.VerificationStatus == VerificationStatus.Approved);
            }
            else if (status == PassportVerificationStatusFilter.Rejected)
            {
                query = query.Where(x => x.d.VerificationStatus == VerificationStatus.Rejected);
            }
        }

        // 2. Apply search filter
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var searchLower = searchTerm.ToLower();
            query = query.Where(x => x.c.Firstname.ToLower().Contains(searchLower) ||
                                     x.c.Lastname.ToLower().Contains(searchLower) ||
                                     x.c.Email.ToLower().Contains(searchLower) ||
                                     x.c.PhoneNumber.Contains(searchLower) ||
                                     x.c.PassportNumber.Contains(searchLower));
        }

        // 3. Get overall counts
        var counts = await GetPassportVerificationCountsAsync();

        // 4. Apply pagination & ordering
        query = query.OrderByDescending(x => x.d.CreatedAt);
        
        var pagedItems = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var passports = pagedItems.Select(x => new PassportVerificationItem
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
            },
            Status = x.d.VerificationStatus.ToString().ToLower(),
            Gender = x.c.Gender,
            OcrConfidenceScore = Math.Round(x.p.OcrConfidenceScore, 2),
            ManualReviewRequired = x.p.ManualReviewRequired
        }).ToList();

        return new PassportVerificationListResponse
        {
            PendingCount = counts.PendingCount,
            ApprovedCount = counts.ApprovedCount,
            RejectedCount = counts.RejectedCount,
            Passports = passports
        };
    }

    public async Task<PassportVerificationCountsResponse> GetPassportVerificationCountsAsync()
    {
        var baseQuery = from d in _db.Documents
                        join c in _db.Customers on d.OwnerId equals c.CustomerId
                        join p in _db.PassportValidations on d.DocumentId equals p.DocumentId
                        where d.OwnerType == DocumentOwnerType.Customer && d.DocumentType == DocumentType.Passport
                           && p.ManualReviewRequired == true
                        select d.VerificationStatus;

        var statuses = await baseQuery.ToListAsync();

        return new PassportVerificationCountsResponse
        {
            PendingCount = statuses.Count(s => s == VerificationStatus.Pending || s == VerificationStatus.UnderReview),
            ApprovedCount = statuses.Count(s => s == VerificationStatus.Approved),
            RejectedCount = statuses.Count(s => s == VerificationStatus.Rejected)
        };
    }

    public async Task<PassportVerificationDetailsResponse?> GetPassportVerificationDetailsAsync(int documentId)
    {
        var query = from d in _db.Documents
                    join c in _db.Customers on d.OwnerId equals c.CustomerId
                    join p in _db.PassportValidations on d.DocumentId equals p.DocumentId
                    where d.DocumentId == documentId && d.OwnerType == DocumentOwnerType.Customer && d.DocumentType == DocumentType.Passport
                    select new { d, c, p };

        var item = await query.FirstOrDefaultAsync();
        if (item == null) return null;

        return new PassportVerificationDetailsResponse
        {
            DocumentId = item.d.DocumentId,
            CustomerId = item.c.CustomerId,
            CustomerName = $"{item.c.Firstname} {item.c.Lastname}",
            RequestNumber = item.d.DocumentId.ToString(),
            RequestDate = item.d.CreatedAt.ToString("dd/MM/yyyy"),
            Mobile = item.c.PhoneNumber,
            Email = item.c.Email,
            PassportImageUrl = item.d.FilePath,
            Status = item.d.VerificationStatus.ToString().ToLower(),
            Gender = item.c.Gender,
            OcrConfidenceScore = Math.Round(item.p.OcrConfidenceScore, 2),
            ManualReviewRequired = item.p.ManualReviewRequired,
            PassportInfo = new PassportInfoDetails
            {
                PassportNumber = item.c.PassportNumber,
                Nationality = item.c.Nationality,
                DateOfBirth = item.c.DateOfBirth.ToString("dd/MM/yyyy"),
                ExpiryDate = item.c.PassportExpiryDate.ToString("dd/MM/yyyy")
            }
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
            throw new InvalidOperationException("The entered passport number is already registered to another person in the system.");

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
            Message = "Your passport has been successfully verified and your account is now active.",
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
                    "Welcome to Travora - Account Activated",
                    $"<h2>Hello {customer.Firstname} 👋</h2><p>Your data and passport number (<b style='letter-spacing:1px;'>{customer.PassportNumber}</b>) have been manually reviewed and verified successfully.</p><p>Your account is now <b>active</b> and you can start booking and tracking your flights.</p>");
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
            Message = $"Passport rejected. Reason: {request.Reason}",
            IsRead = false,
            SentAt = DateTime.UtcNow
        };
        _db.Notifications.Add(notification);

        await _db.SaveChangesAsync();
        return true;
    }
}
