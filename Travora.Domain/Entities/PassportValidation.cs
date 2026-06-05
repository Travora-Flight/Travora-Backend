using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class PassportValidation : IHasTimestamps
{
    public int ValidationId { get; set; }

    // --- Basic Validation Checks ---
    public bool ExpiryCheckPassed { get; set; }
    public bool FormatCheckPassed { get; set; }
    public bool NameMatchCheck { get; set; }
    public bool BirthDateMatchCheck { get; set; }

    public PassportValidationStatus ValidationStatus { get; set; } = PassportValidationStatus.Pending;
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;

    // --- OCR & Manual Review ---
    public decimal OcrConfidenceScore { get; set; }
    public bool ManualReviewRequired { get; set; } = false;

    // --- MRZ Info ---
    public string? MrzType { get; set; }         // TD1 | TD2 | TD3
    public string? RawMrzText { get; set; }       // raw MRZ lines
    public int? ValidScore { get; set; }          // 0 - 100
    public string? MrzMethod { get; set; }        // direct | ocr | hybrid

    // --- MRZ Check Digits ---
    public string? CheckNumber { get; set; }
    public string? CheckDateOfBirth { get; set; }
    public string? CheckExpirationDate { get; set; }
    public string? CheckComposite { get; set; }

    // --- MRZ Validity Flags ---
    public bool? ValidNumber { get; set; }
    public bool? ValidDateOfBirth { get; set; }
    public bool? ValidExpirationDate { get; set; }
    public bool? ValidComposite { get; set; }

    // --- Extracted Passport Data ---
    public string? ExtractedPassportNumber { get; set; }
    public string? ExtractedSurname { get; set; }
    public string? ExtractedGivenNames { get; set; }
    public string? ExtractedNationality { get; set; }
    public DateTime? ExtractedDateOfBirth { get; set; }
    public DateTime? ExtractedExpiryDate { get; set; }
    public string? ExtractedGender { get; set; }

    // --- Audit ---
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int DocumentId { get; set; }

    // Navigation properties
    public Document Document { get; set; } = null!;
}
