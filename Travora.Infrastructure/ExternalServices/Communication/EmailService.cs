using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Travora.Application.Interfaces.External.Communication;
using Travora.Shared.Settings;

namespace Travora.Infrastructure.ExternalServices.Communication;

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(EmailSettings settings, ILogger<EmailService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            await client.ConnectAsync(_settings.Host, _settings.Port,
                _settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_settings.Username, _settings.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            throw;
        }
    }

    public async Task SendNewEmployeeCredentialsAsync(string adminEmail, string employeeName, string employeeEmail, string tempPassword)
    {
        var subject = "New employee account created — Travora";
        var body = $"""
            <div dir="ltr" style="font-family: 'Segoe UI', Tahoma, sans-serif; max-width: 600px; margin: 0 auto; background: #f8f9fa; padding: 30px; border-radius: 10px;">
                <h2 style="color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 10px;">🆕 New Employee</h2>
                <table style="width: 100%; border-collapse: collapse; margin: 20px 0;">
                    <tr><td style="padding: 8px; font-weight: bold; color: #7f8c8d;">Employee:</td><td style="padding: 8px;">{employeeName}</td></tr>
                    <tr><td style="padding: 8px; font-weight: bold; color: #7f8c8d;">Email:</td><td style="padding: 8px; direction: ltr;">{employeeEmail}</td></tr>
                    <tr><td style="padding: 8px; font-weight: bold; color: #7f8c8d;">Temporary Password:</td><td style="padding: 8px; direction: ltr; font-weight: bold; color: #e74c3c;">{tempPassword}</td></tr>
                </table>
                <p style="color: #e74c3c; font-weight: bold;">⚠️ Please inform the employee of this data. The password is temporary and will be required to be changed upon the first login.</p>
                <hr style="border: none; border-top: 1px solid #ddd; margin: 20px 0;">
                <p style="color: #95a5a6; font-size: 12px; text-align: center;">Travora System • Automated Email</p>
            </div>
            """;

        await SendEmailAsync(adminEmail, subject, body);
    }

    public async Task SendPassportVerificationResultAsync(string customerEmail, string customerName, bool approved, string? reason = null)
    {
        var subject = approved
            ? "✅ Your passport has been successfully verified — Travora"
            : "❌ Passport was not accepted — Travora";

        var statusColor = approved ? "#27ae60" : "#e74c3c";
        var statusIcon = approved ? "✅" : "❌";
        var statusText = approved ? "Verified Successfully" : "Not Accepted";
        var reasonHtml = !approved && !string.IsNullOrEmpty(reason)
            ? $"<p style='color: #e74c3c; margin-top: 15px;'><strong>Reason:</strong> {reason}</p>"
            : "";

        var body = $"""
            <div dir="ltr" style="font-family: 'Segoe UI', Tahoma, sans-serif; max-width: 600px; margin: 0 auto; background: #f8f9fa; padding: 30px; border-radius: 10px;">
                <h2 style="color: {statusColor};">{statusIcon} {statusText}</h2>
                <p>Hello <strong>{customerName}</strong>,</p>
                <p>Your passport has been reviewed.</p>
                {reasonHtml}
                {(approved ? "<p>You can now enjoy all Travora services.</p>" : "<p>Please re-upload the passport with a clear image.</p>")}
                <hr style="border: none; border-top: 1px solid #ddd; margin: 20px 0;">
                <p style="color: #95a5a6; font-size: 12px; text-align: center;">Travora System • Automated Email</p>
            </div>
            """;

        await SendEmailAsync(customerEmail, subject, body);
    }
}
