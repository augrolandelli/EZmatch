using System.Globalization;
using System.Text;
using EZmatchApi.Auth;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

public record AdminClubDto(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    int? ChatwootAccountId,
    int? ChatwootInboxId,
    int ActiveCourts,
    int ActiveOwners,
    int Customers,
    DateTime CreatedAt);

/// <summary>Alta de un club con su primer dueño. Chatwoot se puede vincular después.</summary>
public record CreateClubRequest(
    string Name,
    int? ChatwootAccountId,
    int? ChatwootInboxId,
    string OwnerFullName,
    string OwnerEmail,
    string OwnerPassword);

public record UpdateClubRequest(string Name, bool IsActive, int? ChatwootAccountId, int? ChatwootInboxId);

/// <summary>Para borrar la actividad hay que escribir el nombre del club, tal cual.</summary>
public record ClearActivityRequest(string ConfirmName);

public record ClearActivityResult(int Bookings, int Customers, int Blocks);

public class CreateClubRequestValidator : AbstractValidator<CreateClubRequest>
{
    public CreateClubRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Ingresá el nombre del club.").MaximumLength(120);
        RuleFor(x => x.ChatwootAccountId).GreaterThan(0).When(x => x.ChatwootAccountId is not null).WithMessage("El id de cuenta no es válido.");
        RuleFor(x => x.ChatwootInboxId).GreaterThan(0).When(x => x.ChatwootInboxId is not null).WithMessage("El id de inbox no es válido.");
        RuleFor(x => x.OwnerFullName).NotEmpty().WithMessage("Ingresá el nombre del dueño.").MaximumLength(120);
        RuleFor(x => x.OwnerEmail).NotEmpty().WithMessage("Ingresá el email del dueño.").EmailAddress().WithMessage("El email no es válido.");
        RuleFor(x => x.OwnerPassword).ValidPassword();
    }
}

public class UpdateClubRequestValidator : AbstractValidator<UpdateClubRequest>
{
    public UpdateClubRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Ingresá el nombre del club.").MaximumLength(120);
        RuleFor(x => x.ChatwootAccountId).GreaterThan(0).When(x => x.ChatwootAccountId is not null).WithMessage("El id de cuenta no es válido.");
        RuleFor(x => x.ChatwootInboxId).GreaterThan(0).When(x => x.ChatwootInboxId is not null).WithMessage("El id de inbox no es válido.");
    }
}

public interface IAdminClubService
{
    Task<IReadOnlyList<AdminClubDto>> ListAsync(CancellationToken ct = default);
    Task<AdminClubDto> CreateAsync(CreateClubRequest request, CancellationToken ct = default);

    /// <summary>Desactivar un club hace que el bot lo ignore y que sus usuarios no puedan entrar.</summary>
    Task<AdminClubDto> UpdateAsync(Guid clubId, UpdateClubRequest request, CancellationToken ct = default);

    /// <summary>Borra reservas, clientes y bloqueos (deja canchas, horarios, configuración y usuarios).</summary>
    Task<ClearActivityResult> ClearActivityAsync(Guid clubId, string confirmName, CancellationToken ct = default);
}

/// <summary>Administración de clubes por el operador de EZmatch (SuperAdmin).</summary>
public class AdminClubService(EZmatchDbContext db, IPasswordHasher hasher, ILogger<AdminClubService> logger) : IAdminClubService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AdminClubDto>> ListAsync(CancellationToken ct = default) =>
        await Project(db.Clubs.AsNoTracking().OrderBy(c => c.Name)).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<AdminClubDto> CreateAsync(CreateClubRequest r, CancellationToken ct = default)
    {
        await EnsureInboxFreeAsync(r.ChatwootInboxId, null, ct);
        var email = AuthService.NormalizeEmail(r.OwnerEmail);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new AppException("Ya existe un usuario con ese email.", StatusCodes.Status409Conflict, "email_taken");
        }

        var club = new Club
        {
            Name = r.Name.Trim(),
            Slug = await UniqueSlugAsync(r.Name, ct),
            ChatwootAccountId = r.ChatwootAccountId,
            ChatwootInboxId = r.ChatwootInboxId,
        };
        db.Clubs.Add(club);
        db.Users.Add(new User
        {
            ClubId = club.Id,
            Email = email,
            FullName = r.OwnerFullName.Trim(),
            Role = UserRole.Owner,
            PasswordHash = hasher.Hash(r.OwnerPassword),
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Club {Club} creado ({ClubId}) con dueño {Email}", club.Name, club.Id, email);
        return await GetAsync(club.Id, ct);
    }

    /// <inheritdoc />
    public async Task<AdminClubDto> UpdateAsync(Guid clubId, UpdateClubRequest r, CancellationToken ct = default)
    {
        var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId, ct) ?? throw AppException.NotFound("El club no existe.");
        await EnsureInboxFreeAsync(r.ChatwootInboxId, clubId, ct);

        club.Name = r.Name.Trim();
        club.IsActive = r.IsActive;
        club.ChatwootAccountId = r.ChatwootAccountId;
        club.ChatwootInboxId = r.ChatwootInboxId;
        await db.SaveChangesAsync(ct);
        return await GetAsync(clubId, ct);
    }

    /// <inheritdoc />
    public async Task<ClearActivityResult> ClearActivityAsync(Guid clubId, string confirmName, CancellationToken ct = default)
    {
        var club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clubId, ct)
            ?? throw AppException.NotFound("El club no existe.");
        if (!string.Equals(confirmName?.Trim(), club.Name, StringComparison.Ordinal))
        {
            throw new AppException("Para confirmar, escribí el nombre del club tal cual.", StatusCodes.Status400BadRequest, "confirm_mismatch");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var bookings = await db.Bookings.Where(b => b.ClubId == clubId).ExecuteDeleteAsync(ct);
        var blocks = await db.Blocks.Where(b => b.Court.ClubId == clubId).ExecuteDeleteAsync(ct);
        var customers = await db.Customers.Where(c => c.ClubId == clubId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogWarning("Actividad del club {ClubId} borrada: {Bookings} reservas, {Customers} clientes, {Blocks} bloqueos",
            clubId, bookings, customers, blocks);
        return new ClearActivityResult(bookings, customers, blocks);
    }

    private async Task<AdminClubDto> GetAsync(Guid clubId, CancellationToken ct) =>
        await Project(db.Clubs.AsNoTracking().Where(c => c.Id == clubId)).FirstAsync(ct);

    private IQueryable<AdminClubDto> Project(IQueryable<Club> clubs) =>
        clubs.Select(c => new AdminClubDto(
            c.Id, c.Name, c.Slug, c.IsActive, c.ChatwootAccountId, c.ChatwootInboxId,
            db.Courts.Count(x => x.ClubId == c.Id && x.IsActive),
            db.Users.Count(u => u.ClubId == c.Id && u.Role == UserRole.Owner && u.IsActive),
            db.Customers.Count(x => x.ClubId == c.Id),
            c.CreatedAt));

    /// <summary>Un inbox de Chatwoot atiende a un solo club.</summary>
    private async Task EnsureInboxFreeAsync(int? inboxId, Guid? exceptClubId, CancellationToken ct)
    {
        if (inboxId is null) return;
        var owner = await db.Clubs.AsNoTracking()
            .Where(c => c.ChatwootInboxId == inboxId && c.Id != exceptClubId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);
        if (owner is not null)
        {
            throw new AppException($"El inbox {inboxId} ya está vinculado a {owner}.", StatusCodes.Status409Conflict, "inbox_taken");
        }
    }

    /// <summary>"Pádel Norte!" → "padel-norte"; si existe, "padel-norte-2", etc.</summary>
    private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
    {
        var normalized = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        }
        var baseSlug = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (baseSlug.Length == 0) baseSlug = "club";
        if (baseSlug.Length > 50) baseSlug = baseSlug[..50].TrimEnd('-');

        var slug = baseSlug;
        for (var n = 2; await db.Clubs.AnyAsync(c => c.Slug == slug, ct); n++) slug = $"{baseSlug}-{n}";
        return slug;
    }
}
