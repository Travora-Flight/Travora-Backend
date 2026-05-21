namespace Travora.Domain.Enums;

public enum BaggageTrackingStatus
{
    Registered = 1,           // Order confirmed, bags registered
    PickedUp = 2,             // Driver picked up bags from customer (Check-in)
    ArrivedAtAirport = 3,     // Bags arrived at departure airport (BaggageHandler received)
    AtSecurity = 4,           // Bags passed airport security check
    AtTerminal = 5,           // Bags at airport terminal
    AtGate = 6,               // Bags at boarding gate
    LoadedOnAircraft = 7,     // Bags loaded on the aircraft
    Arrived = 8,              // Aircraft arrived at destination
    AtCustoms = 9,            // Bags at destination customs
    OnBelt = 10,              // Bags on baggage belt
    AtBaggageOffice = 11,     // Bags moved to lost baggage office
    OutForDelivery = 12,      // Driver out for delivery to customer
    Delivered = 13,           // Delivered to customer
    Cancelled = 14
}
