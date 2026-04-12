using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Passport;

namespace Travora.API.SwaggerExamples.Admin;

public class PassportVerificationListResponseExample : IExamplesProvider<PassportVerificationListResponse>
{
    public PassportVerificationListResponse GetExamples()
    {
        return new PassportVerificationListResponse
        {
            PendingCount = 5,
            ApprovedCount = 150,
            RejectedCount = 3,
            Passports = new List<PassportVerificationItem>
            {
                new PassportVerificationItem
                {
                    DocumentId = 12,
                    CustomerName = "John Smith",
                    RequestNumber = "REQ-54321",
                    RequestDate = "2026-04-12 10:00",
                    Mobile = "+123456789",
                    Email = "john.smith@example.com",
                    PassportImageUrl = "https://example.com/passport.jpg",
                    PassportInfo = new PassportInfoDetails
                    {
                        PassportNumber = "A1234567",
                        Nationality = "USA",
                        DateOfBirth = "1985-06-15",
                        ExpiryDate = "2030-06-14"
                    },
                    Status = "pending",
                    OcrConfidenceScore = 95.5m,
                    ManualReviewRequired = false
                }
            }
        };
    }
}
