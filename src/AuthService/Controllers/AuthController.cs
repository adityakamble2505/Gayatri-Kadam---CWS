using GreenCarWash.AuthService.Auth;
using GreenCarWash.AuthService.DTOs;
using GreenCarWash.AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.AuthService.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(UserResponseDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<UserResponseDto>> Register(RegisterDto dto, CancellationToken cancellationToken)
    {
        var user = await _auth.RegisterAsync(dto, cancellationToken);
        return Created($"/api/auth/user/{user.UserId}", user);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _auth.LoginAsync(dto, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponseDto>> Refresh(RefreshRequestDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _auth.RefreshAsync(dto, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutRequestDto dto, CancellationToken cancellationToken)
    {
        await _auth.LogoutAsync(dto, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = AdminOrServiceHandler.PolicyName)]
    [HttpGet("user/{id:int}")]
    [ProducesResponseType(typeof(UserResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserResponseDto>> GetUser(int id, CancellationToken cancellationToken)
    {
        return Ok(await _auth.GetUserAsync(id, cancellationToken));
    }
}
