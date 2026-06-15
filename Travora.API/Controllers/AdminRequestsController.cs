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
    [ProducesResponseType(typeof(RequestPagedResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRequestsAsync(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] Travora.Domain.Enums.RequestTimeFilter filter = Travora.Domain.Enums.RequestTimeFilter.All,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await _requestService.GetRequestsAsync(search, filter, status, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{orderId}")]
    [ProducesResponseType(typeof(RequestDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRequestDetailsAsync(int orderId)
    {
        try
        {
            var result = await _requestService.GetRequestDetailsAsync(orderId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("unassigned-services")]
    [ProducesResponseType(typeof(IEnumerable<UnassignedServiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnassignedServicesAsync()
    {
        var result = await _requestService.GetUnassignedServicesAsync();
        return Ok(result);
    }

    [HttpGet("services/{orderServiceId}/available-employees")]
    [ProducesResponseType(typeof(IEnumerable<AvailableEmployeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailableEmployeesAsync(int orderServiceId)
    {
        try
        {
            var result = await _requestService.GetAvailableEmployeesForServiceAsync(orderServiceId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPatch("{orderId}/assign")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AssignEmployeeAsync(int orderId, [FromBody] AssignEmployeeRequest request)
    {
        try
        {
            await _requestService.AssignEmployeeAsync(orderId, request);
            return Ok(new { success = true });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
