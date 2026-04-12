using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Employee.Baggage;
using Travora.Application.Interfaces.Services.Employee;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/employee/baggage")]
[Authorize(Roles = "employee")]
public class EmployeeBaggageController : ControllerBase
{
    private readonly IEmployeeBaggageService _baggageService;

    public EmployeeBaggageController(IEmployeeBaggageService baggageService)
    {
        _baggageService = baggageService;
    }

    [HttpPost("scan")]
    [ProducesResponseType(typeof(BaggageScanResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ScanBaggage([FromBody] BaggageScanRequest request)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _baggageService.ScanBaggageAsync(employeeId, request);
        return Ok(response);
    }

    [HttpPost("{baggageId}/lock")]
    [ProducesResponseType(typeof(LockBaggageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> LockBaggage(int baggageId, [FromBody] LockBaggageRequest request)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _baggageService.AssignLockCodeAsync(employeeId, baggageId, request);
        return Ok(response);
    }

    [HttpPost("{baggageId}/photos")]
    [ProducesResponseType(typeof(BaggagePhotoResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadPhotos(int baggageId, [FromForm] List<IFormFile> photos)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _baggageService.UploadBaggagePhotosAsync(employeeId, baggageId, photos);
        return Ok(response);
    }

    [HttpPost("checkpoint-update")]
    [ProducesResponseType(typeof(CheckpointUpdateResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckpointUpdate([FromBody] CheckpointUpdateRequest request)
    {
        var employeeId = int.Parse(User.FindFirstValue("employeeId")!);
        var response = await _baggageService.UpdateCheckpointAsync(employeeId, request);
        return Ok(response);
    }
}
