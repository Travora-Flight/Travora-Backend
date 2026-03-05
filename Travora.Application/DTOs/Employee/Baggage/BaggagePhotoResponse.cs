namespace Travora.Application.DTOs.Employee.Baggage;

public class BaggagePhotoResponse
{
    public bool Success { get; set; }
    public int BaggageId { get; set; }
    public string? TagNumber { get; set; }
    public int PhotosAdded { get; set; }
    public int TotalPhotos { get; set; }
    public List<string> Photos { get; set; } = new();
}
