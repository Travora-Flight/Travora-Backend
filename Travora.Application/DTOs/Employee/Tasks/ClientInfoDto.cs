namespace Travora.Application.DTOs.Employee.Tasks;

public class ClientInfoDto
{
    public string Name { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;

    // Customs-related fields (populated only for ArrivalBaggageHandling tasks)
    public string? PassportNumber { get; set; }
    public string? Nationality { get; set; }
    public string? PassportExpiryDate { get; set; }
}
