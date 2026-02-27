using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Travora.Application.DTOs.Auth;
using Travora.Application.Interfaces;

namespace Travora.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("admin/login")]
    public async Task<IActionResult> AdminLogin([FromBody] LoginRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = Request.Headers.UserAgent.ToString();
        var response = await _authService.LoginAdminAsync(request.Email, request.Password, ip, ua);
        return Ok(response);
    }

    [HttpPost("employee/login")]
    public async Task<IActionResult> EmployeeLogin([FromBody] LoginRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = Request.Headers.UserAgent.ToString();
        var response = await _authService.LoginEmployeeAsync(request.Email, request.Password, ip, ua);
        return Ok(response);
    }

    [HttpPost("customer/login")]
    public async Task<IActionResult> CustomerLogin([FromBody] LoginRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = Request.Headers.UserAgent.ToString();
        var response = await _authService.LoginCustomerAsync(request.Email, request.Password, ip, ua);
        return Ok(response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var response = await _authService.RefreshTokenAsync(request.RefreshToken);
        return Ok(response);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        await _authService.LogoutAsync(request.RefreshToken);
        return Ok(new { success = true });
    }
}
