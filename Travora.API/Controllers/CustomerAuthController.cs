using Microsoft.AspNetCore.Mvc;
using Travora.Application.DTOs.Customer.Auth;
using Travora.Application.Interfaces.Services.Customer;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/auth/customer")]
public class CustomerAuthController : ControllerBase
{
    private readonly ICustomerAuthService _authService;

    public CustomerAuthController(ICustomerAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register/step1")]
    [ProducesResponseType(typeof(CustomerAuthMessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RegisterStep1([FromBody] RegisterStep1Request request)
    {
        var result = await _authService.RegisterStep1Async(request);
        return Ok(result);
    }

    [HttpPost("register/step2")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RegisterStep2([FromForm] RegisterStep2Request request)
    {
        var result = await _authService.RegisterStep2Async(request);
        return Ok(result);
    }

    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(CustomerAuthMessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var result = await _authService.ForgotPasswordAsync(request.Email);
        return Ok(result);
    }

    [HttpPost("verify-otp")]
    [ProducesResponseType(typeof(VerifyOtpResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        var result = await _authService.VerifyOtpAsync(request);
        return Ok(result);
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(CustomerAuthMessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _authService.ResetPasswordAsync(request);
        return Ok(result);
    }

    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(CustomerAuthMessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        var result = await _authService.VerifyEmailAsync(request);
        return Ok(result);
    }

    [HttpPost("resend-verification-email")]
    [ProducesResponseType(typeof(CustomerAuthMessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendVerifyEmailRequest request)
    {
        var result = await _authService.ResendVerificationEmailAsync(request);
        return Ok(result);
    }
}

public class CustomerAuthMessageResponse
{
    public string Message { get; set; } = string.Empty;
}
