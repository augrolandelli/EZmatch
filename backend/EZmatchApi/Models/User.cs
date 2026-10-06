namespace EZmatchApi.Models;

/// <summary>
/// Roles del panel. SuperAdmin (operador de EZmatch) no pertenece a ningún club;
/// Owner y Staff pertenecen a uno solo.
/// </summary>
public enum UserRole
{
    SuperAdmin,
    Owner,
    Staff,
}

/// <summary>Usuario del panel web. Los jugadores no tienen usuario: se identifican por teléfono.</summary>
public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Null solo para SuperAdmin.</summary>
    public Guid? ClubId { get; set; }
    public Club? Club { get; set; }

    /// <summary>Siempre en minúsculas.</summary>
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>
/// Refresh token con rotación: al usarse queda revocado y se emite uno nuevo.
/// Se guarda solo el hash SHA-256: un volcado de la base no permite robar sesiones.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    public bool IsActiveAt(DateTime utcNow) => RevokedAt is null && utcNow < ExpiresAt;
}
