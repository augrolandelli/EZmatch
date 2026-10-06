using EZmatchApi.Auth;
using EZmatchApi.Dtos;
using EZmatchApi.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EZmatchApi.Controllers;

/// <summary>Autenticación del panel. No hay registro público: los usuarios los crea el SuperAdmin o el dueño.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Inicia sesión y devuelve access + refresh token.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        LoginRequest request, [FromServices] IValidator<LoginRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Ok(await authService.LoginAsync(request, ct));
    }

    /// <summary>Rota el refresh token y devuelve un par nuevo.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        RefreshRequest request, [FromServices] IValidator<RefreshRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Ok(await authService.RefreshAsync(request.RefreshToken, ct));
    }

    /// <summary>Cierra la sesión revocando el refresh token.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await authService.LogoutAsync(request.RefreshToken, ct);
        }
        return NoContent();
    }

    /// <summary>Usuario autenticado.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        Ok(await authService.GetMeAsync(currentUser.UserId, ct));

    /// <summary>Cambia la contraseña propia. Cierra las demás sesiones y devuelve una nueva.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request, [FromServices] IValidator<ChangePasswordRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Ok(await authService.ChangePasswordAsync(currentUser.UserId, request, ct));
    }
}
