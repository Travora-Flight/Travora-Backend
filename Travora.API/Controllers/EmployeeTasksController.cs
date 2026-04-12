using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.Interfaces.Services.Employee;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/employee/tasks")]
[Authorize(Roles = "employee")]
public class EmployeeTasksController : ControllerBase
{
    private readonly IEmployeeTaskService _taskService;

    public EmployeeTasksController(IEmployeeTaskService taskService)
    {
        _taskService = taskService;
    }

    [HttpGet("{orderServiceId}")]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Employee.Tasks.TaskDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTaskDetail(int orderServiceId)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _taskService.GetTaskDetailAsync(employeeId, orderServiceId);
        return Ok(response);
    }

    [HttpPatch("{orderServiceId}/start")]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Employee.Tasks.TaskActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartTask(int orderServiceId)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _taskService.StartTaskAsync(employeeId, orderServiceId);
        return Ok(response);
    }

    [HttpPatch("{orderServiceId}/complete")]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Employee.Tasks.TaskActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CompleteTask(int orderServiceId)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _taskService.CompleteTaskAsync(employeeId, orderServiceId);
        return Ok(response);
    }
}
