namespace Travora.Application.DTOs.Customer.Auth;

public class PassportOcrResult
{
    public int ValidScore { get; set; }
    public string? Number { get; set; }
    public string? Surname { get; set; }
    public string? Names { get; set; }
    public string? DateOfBirthFormatted { get; set; }
    public string? ExpirationDateFormatted { get; set; }
    public string? Nationality { get; set; }
    public string? SexFormatted { get; set; }
    public string? MrzType { get; set; }
    public string? RawText { get; set; }
    public string? Method { get; set; }
    public string? Error { get; set; }

    // Check digits
    public string? CheckNumber { get; set; }
    public string? CheckDateOfBirth { get; set; }
    public string? CheckExpirationDate { get; set; }
    public string? CheckComposite { get; set; }

    // Validity flags
    public bool? ValidNumber { get; set; }
    public bool? ValidDateOfBirth { get; set; }
    public bool? ValidExpirationDate { get; set; }
    public bool? CustomValidComposite { get; set; }
}
