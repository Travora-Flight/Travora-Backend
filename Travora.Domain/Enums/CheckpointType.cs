namespace Travora.Domain.Enums;

public enum CheckpointType
{
    PickupPoint = 1,       // Pickup point from customer
    Customs = 2,           // Customs
    SecurityCheck = 3,     // Security check
    AirportTerminal = 4,   // Airport terminal
    AirportGate = 5,       // Boarding gate
    AirportBaggageBelt = 6,// Airport baggage belt
    DeliveryPoint = 7,     // Delivery point to customer
    TransitHub = 8,        // Transit hub / Intermediate transport center
    BaggageOffice = 9      // Lost baggage office at airport
}
