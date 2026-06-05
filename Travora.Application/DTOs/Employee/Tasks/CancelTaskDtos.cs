namespace Travora.Application.DTOs.Employee.Tasks;

/// <summary>
/// Predefined reason for employee-initiated order cancellation.
/// </summary>
public class CancelReasonDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class EmployeeCancelTaskRequest
{
    /// <summary>Predefined reason ID from GET /cancel-reasons.</summary>
    public int ReasonId { get; set; }

    /// <summary>Optional free-text notes from the employee.</summary>
    public string? Notes { get; set; }
}

public class EmployeeCancelTaskResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
    public string? RefundType { get; set; }
}
