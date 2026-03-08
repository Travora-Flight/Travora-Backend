using Travora.Application.DTOs.Customer.Auth;

namespace Travora.Application.Interfaces.Services.Customer;

public interface IPassportOcrService
{
    Task<PassportOcrResult> ExtractPassportDataAsync(string imagePath);
}
