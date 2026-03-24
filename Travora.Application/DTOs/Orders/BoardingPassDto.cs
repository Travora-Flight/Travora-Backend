namespace Travora.Application.DTOs.Orders;

public class BoardingPassResponse
{
    public List<BoardingPassItem> BoardingPasses { get; set; } = new();
}

public class BoardingPassItem
{
    public string AirlineName { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string FromCity { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string DepartureTime { get; set; } = string.Empty;
    public string ArrivalTime { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public string SeatNumber { get; set; } = string.Empty;
    public string Terminal { get; set; } = string.Empty;
    public string Gate { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string BoardingTime { get; set; } = string.Empty;
    public string FlightDate { get; set; } = string.Empty;
    public string BarcodeData { get; set; } = string.Empty;
}
