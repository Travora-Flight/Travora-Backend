using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Orders;

namespace Travora.API.SwaggerExamples.Orders;

public class OrderDetailsResponseExample : IExamplesProvider<OrderDetailsResponse>
{
    public OrderDetailsResponse GetExamples()
    {
        return new OrderDetailsResponse
        {
            OrderId = 502,
            PackageName = "Door To Door",
            Status = "Out For Delivery",
            From = "Cairo",
            To = "Dubai",
            NumberOfBags = 3,
            TotalWeight = 65.5m,
            NumberOfPassengers = 2,
            CanCancel = false,
            HasBoardingPass = true,
            Appointment = new AppointmentDto
            {
                Pickup = new AppointmentSlot
                {
                    Date = "Monday, April 13, 2026",
                    Time = "10:00-12:00"
                },
                Delivery = new AppointmentSlot
                {
                    Date = "Tuesday, April 14, 2026",
                    Time = "14:00-16:00"
                }
            },
            TrackingStatus = new List<TrackingStepDto>
            {
                new TrackingStepDto
                {
                    Step = "Order Confirmed",
                    Description = "Order confirmed and service scheduled",
                    Timestamp = DateTime.UtcNow.AddDays(-2),
                    IsDone = true
                },
                new TrackingStepDto
                {
                    Step = "Picked Up",
                    Description = "Bags picked up from your location",
                    Timestamp = DateTime.UtcNow.AddDays(-1),
                    IsDone = true
                },
                new TrackingStepDto
                {
                    Step = "Out for Delivery",
                    Description = "Bags are on the way",
                    Timestamp = DateTime.UtcNow,
                    IsDone = true
                },
                new TrackingStepDto
                {
                    Step = "Delivered",
                    Description = "Successfully delivered",
                    Timestamp = null,
                    IsDone = false
                }
            }
        };
    }
}
