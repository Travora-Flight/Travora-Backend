namespace Travora.Application.DTOs.Admin.Employees;

public class ResetPasswordResponse
{
    public bool Success { get; set; }
    public string TempPassword { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
