namespace Travora.Application.DTOs.Admin.Requests;

public class AssignEmployeeRequest
{
    public int EmployeeId { get; set; }
    public int? OrderServiceId { get; set; } // Optional: assign to specific service
}
