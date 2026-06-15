namespace Travora.Application.DTOs.Employee.Tasks;

public class CancelReasonDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class EmployeeCancelTaskRequest
{
    public int ReasonId { get; set; }
    public string? Notes { get; set; }
}

public class EmployeeCancelTaskResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
    public string? RefundType { get; set; }
}
