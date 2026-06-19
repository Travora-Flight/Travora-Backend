using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Flight : IHasTimestamps
{
    public int FlightId { get; set; }

    // Flight Basic Info
    public string FlightNumber { get; set; } = string.Empty;
    public string FlightIataNumber { get; set; } = string.Empty;
    public string FlightIcaoNumber { get; set; } = string.Empty;
    public FlightStatus FlightStatus { get; set; } = FlightStatus.Scheduled;
    public string FlightType { get; set; } = string.Empty;

    // Departure Info
    public string DepartureIataCode { get; set; } = string.Empty;
    public string DepartureIcaoCode { get; set; } = string.Empty;
    public string? DepartureTerminal { get; set; }
    public string? DepartureGate { get; set; }
    public string? DepartureBaggage { get; set; }
    public int? DepartureDelay { get; set; }
    public DateTime ScheduledDepartureTime { get; set; }
    public DateTime? EstimatedDepartureTime { get; set; }
    public DateTime? ActualDepartureTime { get; set; }
    public DateTime? EstimatedDepartureRunway { get; set; }
    public DateTime? ActualDepartureRunway { get; set; }

    // Arrival Info
    public string ArrivalIataCode { get; set; } = string.Empty;
    public string ArrivalIcaoCode { get; set; } = string.Empty;
    public string? ArrivalTerminal { get; set; }
    public string? ArrivalGate { get; set; }
    public string? ArrivalBaggage { get; set; }
    public int? ArrivalDelay { get; set; }
    public DateTime ScheduledArrivalTime { get; set; }
    public DateTime? EstimatedArrivalTime { get; set; }
    public DateTime? ActualArrivalTime { get; set; }
    public DateTime? EstimatedArrivalRunway { get; set; }
    public DateTime? ActualArrivalRunway { get; set; }

    // Airline Info
    public string AirlineName { get; set; } = string.Empty;
    public string AirlineIataCode { get; set; } = string.Empty;
    public string AirlineIcaoCode { get; set; } = string.Empty;

    // Aircraft Info
    public string? AircraftModelCode { get; set; }
    public string? AircraftModelText { get; set; }
    public string? AircraftRegistrationNumber { get; set; }

    // Schedule Info
    public string? Weekday { get; set; }

    // System Info
    public string DataSource { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int? AirlineId { get; set; }
    public int? DepartureAirportId { get; set; }
    public int? ArrivalAirportId { get; set; }

    // Navigation properties
    public Airline? Airline { get; set; }
    public Airport? DepartureAirport { get; set; }
    public Airport? ArrivalAirport { get; set; }
    public ICollection<SavedFlight> SavedFlights { get; set; } = new List<SavedFlight>();
    public ICollection<FlightPrediction> Predictions { get; set; } = new List<FlightPrediction>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<BoardingPass> BoardingPasses { get; set; } = new List<BoardingPass>();
}
