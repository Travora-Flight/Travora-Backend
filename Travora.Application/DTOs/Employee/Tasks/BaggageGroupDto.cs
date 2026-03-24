namespace Travora.Application.DTOs.Employee.Tasks;

public class BaggageGroupDto
{
    public string OwnerType { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public int BaggageCount { get; set; }
    public List<TaskBagItemDto> Bags { get; set; } = new();
}
