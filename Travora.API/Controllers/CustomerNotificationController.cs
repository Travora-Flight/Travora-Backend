using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Customer.Notifications;
using Travora.Application.Interfaces.Services.Customer;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/customer/notifications")]
[Authorize(Roles = "customer")]
public class CustomerNotificationController : ControllerBase
{
    private readonly ICustomerNotificationService _notificationService;

    public CustomerNotificationController(ICustomerNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private int GetCustomerId() => int.Parse(User.FindFirstValue("customerId")!);

    [HttpGet]
    [ProducesResponseType(typeof(CustomerNotificationsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var customerId = GetCustomerId();
        var response = await _notificationService.GetNotificationsAsync(customerId, page, pageSize);
        return Ok(response);
    }

    [HttpPatch("{notificationId}/read")]
    [ProducesResponseType(typeof(CustomerNotificationGenericResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAsRead(int notificationId)
    {
        var customerId = GetCustomerId();
        await _notificationService.MarkAsReadAsync(customerId, notificationId);
        return Ok(new CustomerNotificationGenericResponse { Success = true, Message = "Notification marked as read" });
    }

    [HttpPatch("read-all")]
    [ProducesResponseType(typeof(CustomerNotificationGenericResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var customerId = GetCustomerId();
        await _notificationService.MarkAllAsReadAsync(customerId);
        return Ok(new CustomerNotificationGenericResponse { Success = true, Message = "All notifications marked as read" });
    }
}

public class CustomerNotificationGenericResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
