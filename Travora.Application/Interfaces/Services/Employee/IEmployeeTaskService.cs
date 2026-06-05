using Travora.Application.DTOs.Employee.Tasks;

namespace Travora.Application.Interfaces.Services.Employee;

public interface IEmployeeTaskService
{
    Task<TaskDetailResponse> GetTaskDetailAsync(int employeeId, int orderServiceId);
    Task<TaskActionResponse> StartTaskAsync(int employeeId, int orderServiceId);
    Task<TaskActionResponse> CompleteTaskAsync(int employeeId, int orderServiceId);
    Task<CompletedTasksResponse> GetCompletedTasksAsync(int employeeId, int page, int pageSize);
    List<CancelReasonDto> GetCancelReasons();
    Task<EmployeeCancelTaskResponse> CancelTaskAsync(int employeeId, int orderServiceId, EmployeeCancelTaskRequest request);
}
