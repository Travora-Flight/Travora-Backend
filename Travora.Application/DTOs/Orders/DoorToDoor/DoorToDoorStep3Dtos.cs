namespace Travora.Application.DTOs.Orders.DoorToDoor;

public class ValidateBaggageResponse
{
    public bool IsValid { get; set; }
    public int Expected { get; set; }
    public int Actual { get; set; }
    public int TotalBaggageCount { get; set; }
    public List<BaggageBreakdown> Breakdown { get; set; } = new();
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BaggageBreakdown
{
    public string TicketNumber { get; set; } = string.Empty;
    public int BaggageCount { get; set; }
}

public class ResolveLocationRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string LocationType { get; set; } = "pickup"; // pickup or delivery
}

public class ResolveLocationResponse
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string FormattedAddress { get; set; } = string.Empty;
    public string? StreetAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public string LocationType { get; set; } = "pickup";
}
