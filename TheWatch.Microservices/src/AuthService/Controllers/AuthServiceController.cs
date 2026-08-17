using Microsoft.AspNetCore.Mvc;
using MediatR;
using Dapr;
using TheWatch.Microservices.Security.AuthService.Models;
using TheWatch.Microservices.Security.AuthService.Services;

namespace TheWatch.Microservices.Security.AuthService.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthServiceController : ControllerBase
{
    private readonly ILogger<AuthServiceController> _logger;
    private readonly IAuthService _authService;

    public AuthServiceController(ILogger<AuthServiceController> logger, IAuthService authService)
    {
        _logger = logger;
        _authService = authService;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "AuthService", domain = "Security", status = "Healthy", timestamp = DateTime.UtcNow });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var (success, error, response) = await _authService.RegisterAsync(request);
        if (!success)
        {
            return BadRequest(new { error });
        }
        _logger.LogInformation("Registered new user: {Username} ({Role})", request.Username, request.Role);
        return Created($"/api/v1/auth/users/{response!.User.Id}", response);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (success, error, response) = await _authService.LoginAsync(request);
        if (!success)
        {
            return Unauthorized(new { error });
        }
        _logger.LogInformation("User logged in: {Username}", request.Username);
        return Ok(response);
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateToken([FromBody] TokenValidationRequest request)
    {
        var validation = await _authService.ValidateTokenAsync(request.Token);
        if (!validation.IsValid)
        {
            return Unauthorized(validation);
        }
        return Ok(validation);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var (success, error, response) = await _authService.RefreshTokenAsync(request.RefreshToken);
        if (!success)
        {
            return Unauthorized(new { error });
        }
        return Ok(response);
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _authService.GetAllUsersAsync();
        return Ok(users);
    }

    [Topic("thewatch-pubsub", "thewatch.security.events")]
    [HttpPost("events")]
    public IActionResult HandleDomainEvent([FromBody] object eventPayload)
    {
        _logger.LogInformation("Received domain event in AuthService: {Payload}", eventPayload);
        return Ok(new { status = "Processed" });
    }
}