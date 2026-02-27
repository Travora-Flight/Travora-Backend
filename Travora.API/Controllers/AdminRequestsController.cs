using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.Requests;
using Travora.Application.Interfaces;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/requests")]
[Authorize(Roles = "admin")]
public class AdminRequestsController : ControllerBase
{
    private readonly IAdminRequestService _requestService;

    public AdminRequestsController(IAdminRequestService requestService)
    {
        _requestService = requestService;
    }

    [HttpGet]
    public async Task<IActionResult> GetRequestsAsync([FromQuery] string? search, [FromQuery] string? filter, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _requestService.GetRequestsAsync(search, filter, status, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{orderId}")]
    public async Task<IActionResult> GetRequestDetailsAsync(int orderId)
    {
        var result = await _requestService.GetRequestDetailsAsync(orderId);
        return Ok(result);
    }

    [HttpPatch("{orderId}/assign")]
    public async Task<IActionResult> AssignEmployeeAsync(int orderId, [FromBody] AssignEmployeeRequest request)
    {
        await _requestService.AssignEmployeeAsync(orderId, request);
        return Ok(new { success = true });
    }
}
