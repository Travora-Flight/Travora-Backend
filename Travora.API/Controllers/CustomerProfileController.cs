using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Travora.Application.DTOs.Customer.Profile;
using Travora.Application.Interfaces.Services.Customer;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/customer")]
[Authorize(Roles = "Customer,customer")]
public class CustomerProfileController : ControllerBase
{
    private readonly ICustomerProfileService _profileService;

    public CustomerProfileController(ICustomerProfileService profileService)
    {
        _profileService = profileService;
    }

    private int GetCustomerId() => int.Parse(User.FindFirstValue("customerId")!);

    // ENDPOINT 1 — GET profile
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var result = await _profileService.GetProfileAsync(GetCustomerId());
        return Ok(result);
    }

    // ENDPOINT 2 — GET account
    [HttpGet("account")]
    public async Task<IActionResult> GetAccount()
    {
        var result = await _profileService.GetAccountInfoAsync(GetCustomerId());
        return Ok(result);
    }

    // ENDPOINT 3 — PUT account (text fields only, no image)
    [HttpPut("account")]
    public async Task<IActionResult> UpdateAccount([FromBody] UpdateAccountRequest request)
    {
        var (success, message) = await _profileService.UpdateAccountAsync(GetCustomerId(), request, null);
        if (!success) return BadRequest(new { success, message });
        return Ok(new { success, message });
    }

    // ENDPOINT 4 — POST upload profile photo
    [HttpPost("account/photo")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadPhoto(IFormFile photo)
    {
        try
        {
            var result = await _profileService.UploadPhotoAsync(GetCustomerId(), photo);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // ENDPOINT 5 — DELETE profile photo
    [HttpDelete("account/photo")]
    public async Task<IActionResult> DeletePhoto()
    {
        try
        {
            var (success, message) = await _profileService.DeletePhotoAsync(GetCustomerId());
            if (!success) return BadRequest(new { success, message });
            return Ok(new { success, message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ENDPOINT 6 — GET settings
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var result = await _profileService.GetSettingsAsync(GetCustomerId());
        return Ok(result);
    }

    // ENDPOINT 7 — PUT settings
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] CustomerSettingsRequest request)
    {
        await _profileService.UpdateSettingsAsync(GetCustomerId(), request);
        return Ok(new { success = true });
    }

    // ENDPOINT 8 — GET orders
    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders()
    {
        var result = await _profileService.GetOrdersAsync(GetCustomerId());
        return Ok(result);
    }

    // ENDPOINT 9 — GET flights
    [HttpGet("flights")]
    public async Task<IActionResult> GetFlights()
    {
        var result = await _profileService.GetSavedFlightsAsync(GetCustomerId());
        return Ok(result);
    }

    // ENDPOINT 10 — POST save flight
    [HttpPost("flights/{flightId}/save")]
    public async Task<IActionResult> SaveFlight(int flightId)
    {
        var (success, message, savedFlightId) = await _profileService.SaveFlightAsync(GetCustomerId(), flightId);

        if (!success)
        {
            if (message.Contains("غير موجودة")) return NotFound(new { success, message });
            return Conflict(new { success, message });
        }

        return Ok(new { success, savedFlightId });
    }

    // ENDPOINT 11 — DELETE saved flight
    [HttpDelete("flights/{savedFlightId}")]
    public async Task<IActionResult> DeleteSavedFlight(int savedFlightId)
    {
        var (success, message) = await _profileService.RemoveSavedFlightAsync(GetCustomerId(), savedFlightId);
        if (!success) return StatusCode(403, new { success, message });
        return Ok(new { success });
    }

    // ENDPOINT 12 — PATCH toggle notification
    [HttpPatch("flights/{savedFlightId}/toggle-notification")]
    public async Task<IActionResult> ToggleNotification(int savedFlightId)
    {
        var (success, message, notificationEnabled) = await _profileService.ToggleFlightNotificationAsync(GetCustomerId(), savedFlightId);
        if (!success) return NotFound(new { success, message });
        return Ok(new { success, notificationEnabled });
    }
}
