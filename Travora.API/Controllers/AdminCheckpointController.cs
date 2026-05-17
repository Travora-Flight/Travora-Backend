using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Admin.Checkpoints;
using Travora.Application.Interfaces.Services;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/admin/checkpoints")]
[Authorize(Roles = "admin")]
public class AdminCheckpointController : ControllerBase
{
    private readonly IAdminCheckpointService _checkpointService;

    public AdminCheckpointController(IAdminCheckpointService checkpointService)
    {
        _checkpointService = checkpointService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CheckpointResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllCheckpointsAsync()
    {
        var result = await _checkpointService.GetAllCheckpointsAsync();
        return Ok(result);
    }

    [HttpGet("{id}/employees")]
    [ProducesResponseType(typeof(IEnumerable<CheckpointEmployeeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCheckpointEmployeesAsync(int id)
    {
        try
        {
            var result = await _checkpointService.GetCheckpointEmployeesAsync(id);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(CheckpointResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCheckpointAsync([FromBody] CreateCheckpointRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var result = await _checkpointService.CreateCheckpointAsync(request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(CheckpointResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCheckpointAsync(int id, [FromBody] UpdateCheckpointRequest request)
    {
        try
        {
            var result = await _checkpointService.UpdateCheckpointAsync(id, request);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCheckpointAsync(int id)
    {
        try
        {
            await _checkpointService.DeleteCheckpointAsync(id);
            return Ok(new { success = true });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
