namespace Travora.Application.Interfaces.External.Communication;

public interface IEmailService
{
    /// <summary>
    /// إرسال إيميل
    /// </summary>
    Task SendEmailAsync(string toEmail, string subject, string htmlBody);

    /// <summary>
    /// إرسال إيميل بيانات الموظف الجديد للـ Admin
    /// </summary>
    Task SendNewEmployeeCredentialsAsync(string adminEmail, string employeeName, string employeeEmail, string tempPassword);

    /// <summary>
    /// إرسال إشعار تحقق جواز السفر
    /// </summary>
    Task SendPassportVerificationResultAsync(string customerEmail, string customerName, bool approved, string? reason = null);
}
