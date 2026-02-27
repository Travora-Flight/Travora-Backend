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
        var subject = "تم إنشاء حساب موظف جديد — Travora";
        var body = $"""
            <div dir="rtl" style="font-family: 'Segoe UI', Tahoma, sans-serif; max-width: 600px; margin: 0 auto; background: #f8f9fa; padding: 30px; border-radius: 10px;">
                <h2 style="color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 10px;">🆕 موظف جديد</h2>
                <table style="width: 100%; border-collapse: collapse; margin: 20px 0;">
                    <tr><td style="padding: 8px; font-weight: bold; color: #7f8c8d;">الموظف:</td><td style="padding: 8px;">{employeeName}</td></tr>
                    <tr><td style="padding: 8px; font-weight: bold; color: #7f8c8d;">الإيميل:</td><td style="padding: 8px; direction: ltr;">{employeeEmail}</td></tr>
                    <tr><td style="padding: 8px; font-weight: bold; color: #7f8c8d;">الباسورد المؤقت:</td><td style="padding: 8px; direction: ltr; font-weight: bold; color: #e74c3c;">{tempPassword}</td></tr>
                </table>
                <p style="color: #e74c3c; font-weight: bold;">⚠️ يرجى إبلاغ الموظف بهذه البيانات. الباسورد مؤقت وسيُطلب تغييره عند أول تسجيل دخول.</p>
                <hr style="border: none; border-top: 1px solid #ddd; margin: 20px 0;">
                <p style="color: #95a5a6; font-size: 12px; text-align: center;">Travora System • Automated Email</p>
            </div>
            """;

        await SendEmailAsync(adminEmail, subject, body);
    }

    public async Task SendPassportVerificationResultAsync(string customerEmail, string customerName, bool approved, string? reason = null)
    {
        var subject = approved
            ? "✅ تم التحقق من جوازك بنجاح — Travora"
            : "❌ لم يتم قبول جواز السفر — Travora";

        var statusColor = approved ? "#27ae60" : "#e74c3c";
        var statusIcon = approved ? "✅" : "❌";
        var statusText = approved ? "تم التحقق بنجاح" : "لم يتم القبول";
        var reasonHtml = !approved && !string.IsNullOrEmpty(reason)
            ? $"<p style='color: #e74c3c; margin-top: 15px;'><strong>السبب:</strong> {reason}</p>"
            : "";

        var body = $"""
            <div dir="rtl" style="font-family: 'Segoe UI', Tahoma, sans-serif; max-width: 600px; margin: 0 auto; background: #f8f9fa; padding: 30px; border-radius: 10px;">
                <h2 style="color: {statusColor};">{statusIcon} {statusText}</h2>
                <p>مرحباً <strong>{customerName}</strong>،</p>
                <p>تم مراجعة جواز السفر الخاص بك.</p>
                {reasonHtml}
                {(approved ? "<p>يمكنك الآن الاستمتاع بجميع خدمات Travora.</p>" : "<p>يرجى إعادة رفع جواز السفر بصورة واضحة.</p>")}
                <hr style="border: none; border-top: 1px solid #ddd; margin: 20px 0;">
                <p style="color: #95a5a6; font-size: 12px; text-align: center;">Travora System • Automated Email</p>
            </div>
            """;

        await SendEmailAsync(customerEmail, subject, body);
    }
}
