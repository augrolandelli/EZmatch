using EZmatchApi.Auth;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

public interface IClubUserService
{
    Task<IReadOnlyList<ClubUserDto>> ListAsync(Guid clubId, Guid currentUserId, CancellationToken ct = default);
    Task<ClubUserDto> CreateAsync(Guid clubId, Guid currentUserId, CreateClubUserRequest request, CancellationToken ct = default);

    /// <summary>
    /// Edita nombre, rol y estado. Nadie se puede desactivar ni quitar el rol de dueño a sí mismo,
    /// y el club siempre conserva al menos un dueño activo. Desactivar cierra sus sesiones.
    /// </summary>
    Task<ClubUserDto> UpdateAsync(Guid clubId, Guid currentUserId, Guid userId, UpdateClubUserRequest request, CancellationToken ct = default);

    /// <summary>Blanquea la contraseña (la nueva la define el dueño) y cierra las sesiones de ese usuario.</summary>
    Task ResetPasswordAsync(Guid clubId, Guid userId, string password, CancellationToken ct = default);
}

/// <summary>Usuarios del panel de un club (dueños y recepción), administrados por el dueño.</summary>
public class ClubUserService(EZmatchDbContext db, IPasswordHasher hasher, TimeProvider time) : IClubUserService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ClubUserDto>> ListAsync(Guid clubId, Guid currentUserId, CancellationToken ct = default) =>
        (await db.Users.AsNoTracking()
            .Where(u => u.ClubId == clubId)
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .ToListAsync(ct))
        .Select(u => ToDto(u, currentUserId))
        .ToList();

    /// <inheritdoc />
    public async Task<ClubUserDto> CreateAsync(Guid clubId, Guid currentUserId, CreateClubUserRequest r, CancellationToken ct = default)
    {
        if (!await db.Clubs.AnyAsync(c => c.Id == clubId, ct)) throw AppException.NotFound("El club no existe.");

        var email = AuthService.NormalizeEmail(r.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new AppException("Ya existe un usuario con ese email.", StatusCodes.Status409Conflict, "email_taken");
        }

        var user = new User
        {
            ClubId = clubId,
            Email = email,
            FullName = r.FullName.Trim(),
            Role = r.Role,
            PasswordHash = hasher.Hash(r.Password),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return ToDto(user, currentUserId);
    }

    /// <inheritdoc />
    public async Task<ClubUserDto> UpdateAsync(
        Guid clubId, Guid currentUserId, Guid userId, UpdateClubUserRequest r, CancellationToken ct = default)
    {
        var user = await FindAsync(clubId, userId, ct);

        if (user.Id == currentUserId && (!r.IsActive || r.Role != user.Role))
        {
            throw new AppException("No podés desactivarte ni cambiarte el rol a vos mismo.",
                StatusCodes.Status400BadRequest, "cannot_change_self");
        }

        var losesOwner = user.Role == UserRole.Owner && user.IsActive && (r.Role != UserRole.Owner || !r.IsActive);
        if (losesOwner && !await db.Users.AnyAsync(
                u => u.ClubId == clubId && u.Id != user.Id && u.Role == UserRole.Owner && u.IsActive, ct))
        {
            throw new AppException("El club tiene que tener al menos un dueño activo.",
                StatusCodes.Status400BadRequest, "last_owner");
        }

        var deactivating = user.IsActive && !r.IsActive;
        user.FullName = r.FullName.Trim();
        user.Role = r.Role;
        user.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        if (deactivating) await RevokeSessionsAsync(user.Id, ct);
        return ToDto(user, currentUserId);
    }

    /// <inheritdoc />
    public async Task ResetPasswordAsync(Guid clubId, Guid userId, string password, CancellationToken ct = default)
    {
        var user = await FindAsync(clubId, userId, ct);
        user.PasswordHash = hasher.Hash(password);
        await db.SaveChangesAsync(ct);
        await RevokeSessionsAsync(user.Id, ct);
    }

    private async Task<User> FindAsync(Guid clubId, Guid userId, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.ClubId == clubId, ct)
        ?? throw AppException.NotFound("El usuario no existe.");

    private Task RevokeSessionsAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private static ClubUserDto ToDto(User u, Guid currentUserId) =>
        new(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt, u.LastLoginAt, u.Id == currentUserId);
}
