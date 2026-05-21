namespace Travora.Domain.Enums;

public enum ExecutionPhase
{
    Pickup = 1,              // Driver picks up bags from customer
    DepartureCheckin = 2,    // BaggageHandler checks bags at departure airport
    ArrivalCheckin = 3,      // BaggageHandler receives bags at destination airport (customs)
    Delivery = 4,            // Driver delivers bags to customer
    Tracking = 5             // Tracking-only (no employee involvement)
}
