using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Flights.Tracker;

namespace Travora.API.SwaggerExamples.Flights;

public class FlightDetailsResponseExample : IExamplesProvider<FlightDetailsResponse>
{
    public FlightDetailsResponse GetExamples()
    {
        return new FlightDetailsResponse
        {
            FlightIata = "MS739",
            AirlineName = "EgyptAir",
            AirlineLogoUrl = "https://example.com/ms-logo.png",
            From = "CAI",
            FromCity = "Cairo",
            To = "DXB",
            ToCity = "Dubai",
            UtcFrom = "UTC+2",
            UtcTo = "UTC+4",
            Aircraft = new AircraftInfo
            {
                ModelText = "Boeing 737-800",
                Registration = "SU-GCS"
            },
            Speed = 850.5m,
            Altitude = 35000,
            DepartureGate = "G2",
            DepartureTerminal = "Terminal 3",
            ArrivalGate = "D10",
            ArrivalTerminal = "Terminal 1",
            ScheduledDeparture = "2026-04-12T10:00:00Z",
            ActualDeparture = "2026-04-12T10:15:00Z",
            ScheduledArrival = "2026-04-12T14:30:00Z",
            EstimatedArrival = "2026-04-12T14:40:00Z",
            DelayMessage = "Delayed by 10 minutes",
            Status = "En Route",
            CurrentPosition = new FlightPosition
            {
                Latitude = 28.53m,
                Longitude = 34.12m
            },
            FlightTrail = new List<FlightTrailPoint>
            {
                new FlightTrailPoint { Latitude = 30.12m, Longitude = 31.40m, Timestamp = 1712916900 },
                new FlightTrailPoint { Latitude = 29.50m, Longitude = 32.50m, Timestamp = 1712917800 }
            }
        };
    }
}
