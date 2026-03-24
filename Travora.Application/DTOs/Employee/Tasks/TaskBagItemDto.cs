namespace Travora.Application.DTOs.Employee.Tasks;

public class TaskBagItemDto
{
    public int BaggageId { get; set; }
    public string? TagNumber { get; set; }
    public decimal? WeightKg { get; set; }
    public string? Destination { get; set; }
    public string? CurrentStatus { get; set; }
    public bool IsScanned { get; set; }
    public int PhotosCount { get; set; }
}
