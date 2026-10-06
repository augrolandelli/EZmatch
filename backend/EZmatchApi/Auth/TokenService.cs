using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EZmatchApi.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EZmatchApi.Auth;

/// <summary>Configuración de JWT (sección "Jwt"). La Key nunca se commitea.</summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "EZmatchApi";
    public string Audience { get; set; } = "EZmatchPanel";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 30;
}

/// <summary>Nombres de claims propios del token de EZmatch.</summary>
public static class EzClaims
{
    public const string UserId = "sub";
    public const string Role = "role";
    public const string ClubId = "club_id";
    public const string Name = "name";
    public const string Email = "email";
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user);

    /// <summary>Token opaco aleatorio para el cliente y su hash para guardar en la base.</summary>
    (string Token, string Hash) CreateRefreshToken();

    string HashRefreshToken(string token);
}

/// <summary>
/// Emisión de tokens: access token JWT corto + refresh token opaco aleatorio.
/// El JWT usa la hora real (no el TimeProvider inyectable): el middleware lo valida contra el reloj del sistema.
/// </summary>
public class TokenService(IOptions<JwtSettings> options) : ITokenService
{
    private readonly JwtSettings _settings = options.Value;

    /// <inheritdoc />
    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(EzClaims.UserId, user.Id.ToString()),
            new(EzClaims.Role, user.Role.ToString()),
            new(EzClaims.Name, user.FullName),
            new(EzClaims.Email, user.Email),
        };
        if (user.ClubId is not null) claims.Add(new Claim(EzClaims.ClubId, user.ClubId.Value.ToString()));

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_settings.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            NotBefore = now,
            IssuedAt = now,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key)), SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }

    /// <inheritdoc />
    public (string Token, string Hash) CreateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return (token, HashRefreshToken(token));
    }

    /// <inheritdoc />
    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
