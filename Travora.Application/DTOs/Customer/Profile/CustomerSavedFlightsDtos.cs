namespace Travora.Application.DTOs.Customer.Profile;

public class SavedFlightDto
{
    public int SavedFlightId { get; set; }
    public string FlightNumber { get; set; } = string.Empty;
    public string FlightIcao { get; set; } = string.Empty;
    public string Registration { get; set; } = string.Empty;
    public string FromIata { get; set; } = string.Empty;
    public string ToIata { get; set; } = string.Empty;
    public string DepartureCity { get; set; } = string.Empty;
    public string ArrivalCity { get; set; } = string.Empty;
    public string FlightDate { get; set; } = string.Empty;
    public string DepartureTime { get; set; } = string.Empty;
    public string ArrivalTime { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AirlineName { get; set; } = string.Empty;
    public string AirlineLogoUrl { get; set; } = string.Empty;
    public bool NotificationEnabled { get; set; }
}

public class SavedFlightsResponse
{
    public List<SavedFlightDto> SavedFlights { get; set; } = new();
    public string? Message { get; set; }
}

public class ToggleNotificationResponse
{
    public bool Success { get; set; }
    public bool NotificationEnabled { get; set; }
}
