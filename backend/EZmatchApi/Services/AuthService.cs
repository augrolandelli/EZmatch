using EZmatchApi.Auth;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EZmatchApi.Services;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Rota el refresh token. Si se presenta uno ya rotado, se revocan todas las sesiones del usuario.</summary>
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Cambia la contraseña, cierra las demás sesiones y devuelve una sesión nueva.</summary>
    Task<AuthResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
}

/// <summary>Login, rotación de refresh tokens, logout y cambio de contraseña del panel.</summary>
public class AuthService(
    EZmatchDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    IOptions<JwtSettings> jwt,
    TimeProvider time,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(30);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <inheritdoc />
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        var user = await db.Users.Include(u => u.Club).FirstOrDefaultAsync(u => u.Email == email, ct);

        // Mensaje genérico: no revelar si el email existe.
        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
        {
            throw AppException.Unauthorized("Email o contraseña incorrectos.");
        }
        EnsureCanSignIn(user);

        user.LastLoginAt = Now;
        return await CreateSessionAsync(user, ct);
    }

    /// <inheritdoc />
    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = tokens.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u.Club)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
        {
            throw AppException.Unauthorized("La sesión expiró. Volvé a ingresar.");
        }

        if (stored.RevokedAt is not null)
        {
            // Reuso de un token ya rotado: posible robo, salvo que sea una carrera entre dos pestañas
            // que renovaron casi a la vez (período de gracia). Fuera de la gracia se cierran todas las sesiones.
            if (Now - stored.RevokedAt.Value > ReuseGracePeriod)
            {
                await RevokeAllAsync(stored.UserId, ct);
                logger.LogWarning("Reuso de refresh token detectado para el usuario {UserId}", stored.UserId);
            }
            throw AppException.Unauthorized("La sesión expiró. Volvé a ingresar.");
        }

        if (!stored.IsActiveAt(Now))
        {
            throw AppException.Unauthorized("La sesión expiró. Volvé a ingresar.");
        }
        EnsureCanSignIn(stored.User);

        stored.RevokedAt = Now;
        return await CreateSessionAsync(stored.User, ct);
    }

    /// <inheritdoc />
    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = tokens.HashRefreshToken(refreshToken);
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now), ct);
    }

    /// <inheritdoc />
    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Club).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw AppException.Unauthorized("Sesión inválida.");
        return ToDto(user);
    }

    /// <inheritdoc />
    public async Task<AuthResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Club).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw AppException.Unauthorized("Sesión inválida.");

        if (!hasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new AppException("La contraseña actual no es correcta.", StatusCodes.Status400BadRequest, "wrong_password");
        }

        user.PasswordHash = hasher.Hash(request.NewPassword);
        await RevokeAllAsync(user.Id, ct);
        return await CreateSessionAsync(user, ct);
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static UserDto ToDto(User user) =>
        new(user.Id, user.Email, user.FullName, user.Role, user.ClubId, user.Club?.Name);

    private static void EnsureCanSignIn(User user)
    {
        if (!user.IsActive)
        {
            throw AppException.Forbidden("El usuario está desactivado.", "user_inactive");
        }
        if (user.Club is { IsActive: false })
        {
            throw AppException.Forbidden("El club está desactivado.", "club_inactive");
        }
    }

    private async Task<AuthResponse> CreateSessionAsync(User user, CancellationToken ct)
    {
        var (accessToken, expiresAt) = tokens.CreateAccessToken(user);
        var (refreshToken, hash) = tokens.CreateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = Now.AddDays(jwt.Value.RefreshTokenDays),
        });
        await db.SaveChangesAsync(ct);
        return new AuthResponse(accessToken, refreshToken, expiresAt, ToDto(user));
    }

    private Task RevokeAllAsync(Guid userId, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now), ct);
}
