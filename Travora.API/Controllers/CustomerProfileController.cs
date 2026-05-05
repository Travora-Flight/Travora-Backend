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
    [ProducesResponseType(typeof(Travora.Application.DTOs.Customer.Profile.CustomerProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile()
    {
        var result = await _profileService.GetProfileAsync(GetCustomerId());
        return Ok(result);
    }

    // ENDPOINT 2 — GET account
    [HttpGet("account")]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Customer.Profile.CustomerAccountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccount()
    {
        var result = await _profileService.GetAccountInfoAsync(GetCustomerId());
        return Ok(result);
    }

    // ENDPOINT 3 — PUT account (text fields only, no image)
    [HttpPut("account")]
    [ProducesResponseType(typeof(CustomerProfileGenericResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAccount([FromBody] UpdateAccountRequest request)
    {
        var (success, message) = await _profileService.UpdateAccountAsync(GetCustomerId(), request, null);
        if (!success) return BadRequest(new CustomerProfileGenericResponse { Success = success, Message = message });
        return Ok(new CustomerProfileGenericResponse { Success = success, Message = message });
    }

    // ENDPOINT 4 — POST upload profile photo
    [HttpPost("account/photo")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CustomerProfileUploadPhotoResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadPhoto(IFormFile photo)
    {
        try
        {
            var result = await _profileService.UploadPhotoAsync(GetCustomerId(), photo);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new CustomerProfileUploadPhotoResponse { Success = false, PhotoUrl = ex.Message }); // Keeping signature for error too though we could map differently
        }
    }

    // ENDPOINT 5 — DELETE profile photo
    [HttpDelete("account/photo")]
    [ProducesResponseType(typeof(CustomerProfileGenericResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeletePhoto()
    {
        try
        {
            var (success, message) = await _profileService.DeletePhotoAsync(GetCustomerId());
            if (!success) return BadRequest(new CustomerProfileGenericResponse { Success = success, Message = message });
            return Ok(new CustomerProfileGenericResponse { Success = success, Message = message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new CustomerProfileGenericResponse { Success = false, Message = ex.Message });
        }
    }

    // ENDPOINT 6 — GET settings
    [HttpGet("settings")]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Customer.Profile.CustomerSettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings()
    {
        var result = await _profileService.GetSettingsAsync(GetCustomerId());
        return Ok(result);
    }

    // ENDPOINT 7 — PUT settings
    [HttpPut("settings")]
    [ProducesResponseType(typeof(CustomerProfileGenericResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings([FromBody] CustomerSettingsRequest request)
    {
        await _profileService.UpdateSettingsAsync(GetCustomerId(), request);
        return Ok(new CustomerProfileGenericResponse { Success = true, Message = "Settings updated successfully" });
    }

    // ENDPOINT 8 — GET orders
    [HttpGet("orders")]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Customer.Profile.CustomerOrdersResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders()
    {
        var result = await _profileService.GetOrdersAsync(GetCustomerId());
        return Ok(result);
    }

    // ENDPOINT 9 — GET flights
    [HttpGet("flights")]
    [ProducesResponseType(typeof(Travora.Application.DTOs.Customer.Profile.SavedFlightsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFlights()
    {
        var result = await _profileService.GetSavedFlightsAsync(GetCustomerId());
        return Ok(result);
    }

    // ENDPOINT 10 — POST save flight
    [HttpPost("flights/{flightId}/save")]
    [ProducesResponseType(typeof(CustomerProfileSaveFlightResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveFlight(int flightId)
    {
        var (success, message, savedFlightId) = await _profileService.SaveFlightAsync(GetCustomerId(), flightId);

        if (!success)
        {
            if (message.Contains("not found")) return NotFound(new CustomerProfileSaveFlightResponse { Success = success, Message = message });
            return Conflict(new CustomerProfileSaveFlightResponse { Success = success, Message = message });
        }

        return Ok(new CustomerProfileSaveFlightResponse { Success = success, SavedFlightId = savedFlightId });
    }

    // ENDPOINT 11 — DELETE saved flight
    [HttpDelete("flights/{savedFlightId}")]
    [ProducesResponseType(typeof(CustomerProfileGenericResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteSavedFlight(int savedFlightId)
    {
        var (success, message) = await _profileService.RemoveSavedFlightAsync(GetCustomerId(), savedFlightId);
        if (!success) return StatusCode(403, new CustomerProfileGenericResponse { Success = success, Message = message });
        return Ok(new CustomerProfileGenericResponse { Success = success });
    }

    // ENDPOINT 12 — PATCH toggle notification
    [HttpPatch("flights/{savedFlightId}/toggle-notification")]
    [ProducesResponseType(typeof(CustomerProfileToggleNotificationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ToggleNotification(int savedFlightId)
    {
        var (success, message, notificationEnabled) = await _profileService.ToggleFlightNotificationAsync(GetCustomerId(), savedFlightId);
        if (!success) return NotFound(new CustomerProfileToggleNotificationResponse { Success = success, Message = message });
        return Ok(new CustomerProfileToggleNotificationResponse { Success = success, NotificationEnabled = notificationEnabled ?? false });
    }

    // ENDPOINT 13 — POST change password
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(CustomerChangePasswordResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] CustomerChangePasswordRequest request)
    {
        await _profileService.ChangePasswordAsync(GetCustomerId(), request.CurrentPassword, request.NewPassword, request.ConfirmPassword);
        return Ok(new CustomerChangePasswordResponse { Success = true, Message = "Password changed successfully" });
    }
}

public class CustomerProfileGenericResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CustomerProfileUploadPhotoResponse
{
    public bool Success { get; set; }
    public string PhotoUrl { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class CustomerProfileSaveFlightResponse
{
    public bool Success { get; set; }
    public int? SavedFlightId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CustomerProfileToggleNotificationResponse
{
    public bool Success { get; set; }
    public bool NotificationEnabled { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CustomerChangePasswordResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
