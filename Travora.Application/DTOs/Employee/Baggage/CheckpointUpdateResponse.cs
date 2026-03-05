namespace Travora.Application.DTOs.Employee.Baggage;

public class CheckpointUpdateResponse
{
    public bool Success { get; set; }
    public BaggageCheckpointInfoDto? Baggage { get; set; }
}

public class BaggageCheckpointInfoDto
{
    public string TagNumber { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string CheckpointName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string? Notes { get; set; }
}
