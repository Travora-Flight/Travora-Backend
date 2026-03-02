namespace Travora.Domain.Enums;

public enum CheckpointType
{
    PickupPoint = 1,       // نقطة الاستلام من العميل
    Customs = 2,           // الجمارك
    SecurityCheck = 3,     // التفتيش الأمني
    AirportTerminal = 4,   // مبنى المطار
    AirportGate = 5,       // بوابة الصعود للطائرة
    AirportBaggageBelt = 6,// حزام الأمتعة في المطار
    DeliveryPoint = 7,     // نقطة التسليم للعميل
    TransitHub = 8         // مركز الترانزيت / النقل الوسيط
}
