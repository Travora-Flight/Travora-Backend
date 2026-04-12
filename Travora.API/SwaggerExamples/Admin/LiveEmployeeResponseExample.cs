using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.LiveTracker;

namespace Travora.API.SwaggerExamples.Admin;

public class LiveEmployeeResponseExample : IExamplesProvider<LiveEmployeeResponse>
{
    public LiveEmployeeResponse GetExamples()
    {
        return new LiveEmployeeResponse
        {
            Available = 15,
            OnService = 5,
            Employees = new List<LiveEmployeeItem>
            {
                new LiveEmployeeItem
                {
                    EmployeeId = 1,
                    Name = "Ahmed Samy",
                    Code = "DRV-1001",
                    JobRole = "Driver",
                    Status = "Available",
                    CurrentTask = null,
                    Location = "Cairo Airport Terminal 3",
                    Latitude = 30.1121m,
                    Longitude = 31.3995m,
                    IsOnline = true,
                    LastUpdated = "2 mins ago",
                    Mobile = "+201012345678"
                },
                new LiveEmployeeItem
                {
                    EmployeeId = 2,
                    Name = "Mohamed Ali",
                    Code = "BGG-2001",
                    JobRole = "BaggageHandler",
                    Status = "On Service",
                    CurrentTask = "Pickup Order #105",
                    Location = "Maadi, Cairo",
                    Latitude = 29.9602m,
                    Longitude = 31.2569m,
                    IsOnline = true,
                    LastUpdated = "Just now",
                    Mobile = "+201122334455"
                }
            }
        };
    }
}
