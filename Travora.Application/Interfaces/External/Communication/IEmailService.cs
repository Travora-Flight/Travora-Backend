namespace Travora.Application.Interfaces.External.Communication;

public interface IEmailService
{
    /// <summary>
    /// Send email
    /// </summary>
    Task SendEmailAsync(string toEmail, string subject, string htmlBody);

    /// <summary>
    /// Send new employee credentials email to Admin
    /// </summary>
    Task SendNewEmployeeCredentialsAsync(string adminEmail, string employeeName, string employeeEmail, string tempPassword);

    /// <summary>
    /// Send passport verification result notification
    /// </summary>
    Task SendPassportVerificationResultAsync(string customerEmail, string customerName, bool approved, string? reason = null);
}
