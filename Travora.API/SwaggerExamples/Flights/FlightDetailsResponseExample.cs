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
            Status = "En Route",
            DelayMessage = "Delayed by 10 minutes",
            Airline = new FlightDetailAirlineDto
            {
                Name = "EgyptAir",
                Iata = "MS",
                Logo = "https://example.com/ms-logo.png"
            },
            Aircraft = new AircraftInfo
            {
                Model = "Boeing 737-800",
                Registration = "SU-GCS"
            },
            Departure = new FlightDetailAirportDto
            {
                Iata = "CAI",
                Name = "Cairo International Airport",
                City = "Cairo",
                Utc = "UTC+2",
                ScheduledTime = "10:00"
            },
            Arrival = new FlightDetailAirportDto
            {
                Iata = "DXB",
                Name = "Dubai International Airport",
                City = "Dubai",
                Utc = "UTC+4",
                ScheduledTime = "14:30"
            },
            Position = new FlightPosition
            {
                Latitude = 28.53m,
                Longitude = 34.12m
            },
            Trail = new List<FlightTrailPoint>
            {
                new FlightTrailPoint { Latitude = 30.12m, Longitude = 31.40m, Timestamp = 1712916900 },
                new FlightTrailPoint { Latitude = 29.50m, Longitude = 32.50m, Timestamp = 1712917800 }
            }
        };
    }
}
