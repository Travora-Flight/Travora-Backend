using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class PassportValidation : IHasTimestamps
{
    public int ValidationId { get; set; }
    public bool ExpiryCheckPassed { get; set; }
    public bool FormatCheckPassed { get; set; }
    public bool NameMatchCheck { get; set; }
    public bool BirthDateMatchCheck { get; set; }
    public PassportValidationStatus ValidationStatus { get; set; } = PassportValidationStatus.Pending;
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
    public decimal OcrConfidenceScore { get; set; }
    public bool ManualReviewRequired { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int DocumentId { get; set; }

    // Navigation properties
    public Document Document { get; set; } = null!;
}
