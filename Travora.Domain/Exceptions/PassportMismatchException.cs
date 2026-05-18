using System;

namespace Travora.Domain.Exceptions;

/// <summary>
/// Thrown when there is a passport details mismatch or medium-confidence OCR score
/// that can be bypassed by the user (using ForceSubmit = true) to submit for manual admin review.
/// </summary>
public class PassportMismatchException : ArgumentException
{
    public PassportMismatchException(string message) : base(message)
    {
    }
}
