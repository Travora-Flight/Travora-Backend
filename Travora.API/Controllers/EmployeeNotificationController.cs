using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.Interfaces.Services.Employee;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/employee/notifications")]
[Authorize(Roles = "employee")]
public class EmployeeNotificationController : ControllerBase
{
    private readonly IEmployeeNotificationService _notificationService;

    public EmployeeNotificationController(IEmployeeNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Employee.Notifications.EmployeeNotificationsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _notificationService.GetNotificationsAsync(employeeId, page, pageSize);
        return Ok(response);
    }

    [HttpPatch("{notificationId}/read")]
    [ProducesResponseType(typeof(EmployeeNotificationGenericResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAsRead(int notificationId)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        await _notificationService.MarkAsReadAsync(employeeId, notificationId);
        return Ok(new EmployeeNotificationGenericResponse { Success = true });
    }

    [HttpPatch("read-all")]
    [ProducesResponseType(typeof(EmployeeNotificationGenericResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        await _notificationService.MarkAllAsReadAsync(employeeId);
        return Ok(new EmployeeNotificationGenericResponse { Success = true });
    }
}

public class EmployeeNotificationGenericResponse
{
    public bool Success { get; set; }
}
