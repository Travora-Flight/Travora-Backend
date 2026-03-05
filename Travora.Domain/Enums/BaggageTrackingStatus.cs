namespace Travora.Domain.Enums;

public enum BaggageTrackingStatus
{
    Registered = 1,
    PickedUp = 2,
    AtCustoms = 3,
    AtSecurity = 4,
    AtTerminal = 5,
    AtGate = 6,
    LoadedOnAircraft = 7,
    Arrived = 8,
    OnBelt = 9,
    OutForDelivery = 10,
    Delivered = 11,
    Cancelled = 12
}
