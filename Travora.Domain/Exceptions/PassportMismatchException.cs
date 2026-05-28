using System;

namespace Travora.Domain.Exceptions;

/// <summary>
/// Thrown when passport OCR validation fails with a retryable error.
/// The frontend uses RemainingAttempts to inform the user how many retries they have left.
/// </summary>
public class PassportMismatchException : ArgumentException
{
    public int? RemainingAttempts { get; }

    public PassportMismatchException(string message, int? remainingAttempts = null) : base(message)
    {
        RemainingAttempts = remainingAttempts;
    }
}
