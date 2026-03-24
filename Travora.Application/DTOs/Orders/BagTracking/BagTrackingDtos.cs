using Microsoft.AspNetCore.Http;

namespace Travora.Application.DTOs.Orders.BagTracking;

// ===== STEP 1 — validate-flight =====
public class BagTrackingValidateFlightRequest
{
    public string TicketNumber { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string FlightDate { get; set; } = string.Empty;
    public int BaggageCount { get; set; }
}

// ===== STEP 3 — scan-bag =====
public class ScanBagRequest
{
    public string QrData { get; set; } = string.Empty;
    public bool EnteredManually { get; set; }
}

public class ScanBagResponse
{
    public bool Found { get; set; }
    public ScannedBagDto? Bag { get; set; }
    public int TotalScanned { get; set; }
    public int TotalRequired { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ScannedBagDto
{
    public string TagNumber { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public string? Destination { get; set; }
    public DateTime ScannedAt { get; set; }
}

// ===== STEP 4 — upload-bag-photos =====
public class UploadBagPhotosResponse
{
    public string TagNumber { get; set; } = string.Empty;
    public List<string> Photos { get; set; } = new();
    public bool Saved { get; set; }
    public string? ErrorMessage { get; set; }
}
