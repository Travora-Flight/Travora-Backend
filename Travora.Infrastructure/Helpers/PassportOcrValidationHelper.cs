using Travora.Application.DTOs.Customer.Auth;

namespace Travora.Infrastructure.Helpers;

/// <summary>
/// Centralized passport OCR validation logic shared across all services.
/// Eliminates code duplication between DoorToDoor, CarService, and BagTracking companion flows.
/// </summary>
public static class PassportOcrValidationHelper
{
    // ── Score Thresholds ──
    public const int CompanionMinScore = 90;
    public const int CustomerLowThreshold = 65;
    public const int CustomerHighThreshold = 85;

    // ── Retry Policy ──
    public const int MaxRetryAttempts = 3; // 3rd attempt → send to admin

    // ── Error Messages ──
    public static class Messages
    {
        public const string ImageUnclear = "Passport image is unclear. Please upload a clearer image.";
        public const string ExpirationNotVerified = "Passport expiration date could not be verified. Please upload the passport again.";
        public const string PassportExpired = "Your passport has expired.";
        public const string NumberUnclear = "Passport number is unclear. Please upload a clearer image.";
        public const string ExpirationDateUnclear = "The expiration date is unclear. Please make sure the image is clear.";
        public const string ExpiryDateMismatch = "Please make sure the expiry date matches the one in the passport.";
        public const string NumberMismatch = "Please make sure the passport number you entered matches the one in the passport image.";
        public const string AccountUnderReview = "Your account has been submitted for review. You will be notified once verified.";
    }

    /// <summary>
    /// Validates companion passport OCR result. No admin involvement, no retry counting.
    /// Used by DoorToDoor, CarService, and BagTracking.
    /// </summary>
    public static (bool IsValid, string? ErrorMessage) ValidateCompanionPassport(PassportOcrResult? ocrResult)
    {
        if (ocrResult == null || ocrResult.ValidScore < CompanionMinScore)
            return (false, Messages.ImageUnclear);

        if (ocrResult.ValidExpirationDate != true)
            return (false, Messages.ExpirationNotVerified);

        if (DateTime.TryParse(ocrResult.ExpirationDateFormatted, out var expiry) && expiry <= DateTime.UtcNow)
            return (false, Messages.PassportExpired);

        if (ocrResult.ValidNumber != true)
            return (false, Messages.NumberUnclear);

        return (true, null);
    }

    /// <summary>
    /// Determines the customer passport validation outcome based on score range, OCR flags,
    /// user input, and attempt count.
    /// </summary>
    public static CustomerPassportValidationResult ValidateCustomerPassport(
        PassportOcrResult ocrResult,
        string userInputPassportNumber,
        DateTime userInputExpiryDate,
        int currentAttempt)
    {
        int score = ocrResult.ValidScore;

        // ── Score < 65: Always reject, no admin, no attempt counting ──
        if (score < CustomerLowThreshold)
        {
            return CustomerPassportValidationResult.Reject(Messages.ImageUnclear);
        }

        // ── Score 65–85: Medium confidence ──
        if (score <= CustomerHighThreshold)
        {
            return ValidateMediumConfidence(ocrResult, currentAttempt);
        }

        // ── Score > 85: High confidence ──
        return ValidateHighConfidence(ocrResult, userInputPassportNumber, userInputExpiryDate, currentAttempt);
    }

    // ────────────────────────────────────────────────────────────────
    // Medium confidence (65–85)
    // ────────────────────────────────────────────────────────────────
    private static CustomerPassportValidationResult ValidateMediumConfidence(
        PassportOcrResult ocrResult, int currentAttempt)
    {
        if (ocrResult.ValidExpirationDate == true)
        {
            // Check if passport is expired
            if (DateTime.TryParse(ocrResult.ExpirationDateFormatted, out var expiry) && expiry <= DateTime.UtcNow)
                return CustomerPassportValidationResult.Reject(Messages.PassportExpired);

            // Not expired → send to admin immediately (no retry)
            return CustomerPassportValidationResult.SendToAdmin(Messages.AccountUnderReview);
        }

        // ValidExpirationDate is false/null → retryable with attempt counting
        return EvaluateRetryOrAdmin(
            currentAttempt,
            Messages.ExpirationNotVerified);
    }

    // ────────────────────────────────────────────────────────────────
    // High confidence (> 85)
    // ────────────────────────────────────────────────────────────────
    private static CustomerPassportValidationResult ValidateHighConfidence(
        PassportOcrResult ocrResult,
        string userInputPassportNumber,
        DateTime userInputExpiryDate,
        int currentAttempt)
    {
        // Step 1: Check ValidExpirationDate flag
        if (ocrResult.ValidExpirationDate != true)
        {
            return EvaluateRetryOrAdmin(currentAttempt, Messages.ExpirationDateUnclear);
        }

        // Step 2: Check if passport is expired
        if (DateTime.TryParse(ocrResult.ExpirationDateFormatted, out var ocrExpiry) && ocrExpiry <= DateTime.UtcNow)
        {
            return CustomerPassportValidationResult.Reject(Messages.PassportExpired);
        }

        // Step 3: Compare expiry date (user input vs OCR)
        if (ocrExpiry != default && ocrExpiry.Date != userInputExpiryDate.Date)
        {
            return EvaluateRetryOrAdmin(currentAttempt, Messages.ExpiryDateMismatch);
        }

        // Step 4: Check ValidNumber flag
        if (ocrResult.ValidNumber != true)
        {
            return EvaluateRetryOrAdmin(currentAttempt, Messages.NumberUnclear);
        }

        // Step 5: Compare passport number (user input vs OCR)
        string cleanedOcrNumber = (ocrResult.Number ?? "")
            .Replace(" ", "").Replace("-", "").Replace(",", "").Trim().ToUpperInvariant();

        if (!string.Equals(userInputPassportNumber, cleanedOcrNumber, StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateRetryOrAdmin(currentAttempt, Messages.NumberMismatch);
        }

        // ✅ Everything matches
        return CustomerPassportValidationResult.Passed();
    }

    // ────────────────────────────────────────────────────────────────
    // Retry / Admin decision
    // ────────────────────────────────────────────────────────────────
    private static CustomerPassportValidationResult EvaluateRetryOrAdmin(
        int currentAttempt, string retryMessage)
    {
        if (currentAttempt >= MaxRetryAttempts)
            return CustomerPassportValidationResult.SendToAdmin(Messages.AccountUnderReview);

        int remaining = MaxRetryAttempts - currentAttempt;
        return CustomerPassportValidationResult.Retry(retryMessage, remaining);
    }
}

// ── Result Type ──

public enum CustomerPassportOutcome
{
    /// <summary>All validations passed, account should be created as Verified.</summary>
    Passed,

    /// <summary>Hard rejection (e.g., image unreadable, passport expired). User can retry but no attempt counting.</summary>
    Rejected,

    /// <summary>Soft failure — user can retry (attempt counted). Includes remaining attempts.</summary>
    RetryableError,

    /// <summary>Send to admin for manual review. Create account as PendingVerification.</summary>
    AdminReview
}

public class CustomerPassportValidationResult
{
    public CustomerPassportOutcome Outcome { get; private init; }
    public string? Message { get; private init; }
    public int? RemainingAttempts { get; private init; }

    public static CustomerPassportValidationResult Passed()
        => new() { Outcome = CustomerPassportOutcome.Passed };

    public static CustomerPassportValidationResult Reject(string message)
        => new() { Outcome = CustomerPassportOutcome.Rejected, Message = message };

    public static CustomerPassportValidationResult Retry(string message, int remainingAttempts)
        => new() { Outcome = CustomerPassportOutcome.RetryableError, Message = message, RemainingAttempts = remainingAttempts };

    public static CustomerPassportValidationResult SendToAdmin(string message)
        => new() { Outcome = CustomerPassportOutcome.AdminReview, Message = message };
}
