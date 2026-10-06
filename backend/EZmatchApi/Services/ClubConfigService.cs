using System.Globalization;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

public interface IClubConfigService
{
    Task<ClubSettingsDto> GetSettingsAsync(Guid clubId, CancellationToken ct = default);
    Task<ClubSettingsDto> UpdateSettingsAsync(Guid clubId, UpdateClubSettingsRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<CourtDto>> GetCourtsAsync(Guid clubId, CancellationToken ct = default);
    Task<CourtDto> CreateCourtAsync(Guid clubId, SaveCourtRequest request, CancellationToken ct = default);

    /// <summary>Edita una cancha. Desactivarla con reservas futuras da 409: hay que cancelarlas primero.</summary>
    Task<CourtDto> UpdateCourtAsync(Guid clubId, Guid courtId, SaveCourtRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<CourtDto>> ReorderCourtsAsync(Guid clubId, IReadOnlyList<Guid> courtIds, CancellationToken ct = default);

    Task<IReadOnlyList<SlotTemplateDto>> GetSlotsAsync(Guid clubId, Guid courtId, CancellationToken ct = default);

    /// <summary>Reemplaza la grilla entera. Las reservas existentes no se tocan.</summary>
    Task<IReadOnlyList<SlotTemplateDto>> ReplaceSlotsAsync(Guid clubId, Guid courtId, IReadOnlyList<SlotInput> slots, CancellationToken ct = default);

    Task<IReadOnlyList<SlotTemplateDto>> GenerateSlotsAsync(Guid clubId, Guid courtId, GenerateSlotsRequest request, CancellationToken ct = default);

    /// <summary>Copia la grilla de una cancha a otras (reemplaza la de cada destino).</summary>
    Task CopySlotsAsync(Guid clubId, Guid sourceCourtId, IReadOnlyList<Guid> targetCourtIds, CancellationToken ct = default);

    Task<IReadOnlyList<BlockDto>> GetBlocksAsync(Guid clubId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>Un bloqueo por cancha. 409 con las reservas que ya ocupan ese rango.</summary>
    Task<IReadOnlyList<BlockDto>> CreateBlockAsync(Guid clubId, CreateBlockRequest request, CancellationToken ct = default);

    Task DeleteBlockAsync(Guid clubId, Guid blockId, CancellationToken ct = default);
}

/// <summary>Configuración del club desde el panel: datos, políticas, canchas, grilla y bloqueos (spec §4.2).</summary>
public class ClubConfigService(EZmatchDbContext db, TimeProvider time) : IClubConfigService
{
    private const int DayMinutes = 24 * 60;
    private const int WeekMinutes = 7 * DayMinutes;
    private const int DefaultBlocksDays = 60;

    // ---- Club ----

    /// <inheritdoc />
    public async Task<ClubSettingsDto> GetSettingsAsync(Guid clubId, CancellationToken ct = default) =>
        ToDto(await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clubId, ct)
            ?? throw AppException.NotFound("El club no existe."));

    /// <inheritdoc />
    public async Task<ClubSettingsDto> UpdateSettingsAsync(Guid clubId, UpdateClubSettingsRequest r, CancellationToken ct = default)
    {
        var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId, ct)
            ?? throw AppException.NotFound("El club no existe.");

        club.Name = r.Name.Trim();
        club.Address = Clean(r.Address);
        club.Phone = Clean(r.Phone);
        club.AssistantName = Clean(r.AssistantName);
        club.BotInstructions = Clean(r.BotInstructions);
        club.BotShowsPrices = r.BotShowsPrices;
        club.CancellationMinHours = r.CancellationMinHours;
        club.MinLeadMinutes = r.MinLeadMinutes;
        club.BookingHorizonDays = r.BookingHorizonDays;
        club.MaxActiveBookingsPerCustomer = r.MaxActiveBookingsPerCustomer;
        await db.SaveChangesAsync(ct);
        return ToDto(club);
    }

    // ---- Canchas ----

    /// <inheritdoc />
    public async Task<IReadOnlyList<CourtDto>> GetCourtsAsync(Guid clubId, CancellationToken ct = default) =>
        await db.Courts.AsNoTracking()
            .Where(c => c.ClubId == clubId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CourtDto(c.Id, c.Name, c.Sport, c.IsCovered, c.SortOrder, c.IsActive, c.SlotTemplates.Count))
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<CourtDto> CreateCourtAsync(Guid clubId, SaveCourtRequest r, CancellationToken ct = default)
    {
        if (!await db.Clubs.AnyAsync(c => c.Id == clubId, ct)) throw AppException.NotFound("El club no existe.");
        var nextOrder = (await db.Courts.Where(c => c.ClubId == clubId).MaxAsync(c => (int?)c.SortOrder, ct) ?? 0) + 1;
        var court = new Court
        {
            ClubId = clubId, Name = r.Name.Trim(), Sport = r.Sport, IsCovered = r.IsCovered, IsActive = r.IsActive, SortOrder = nextOrder,
        };
        db.Courts.Add(court);
        await db.SaveChangesAsync(ct);
        return new CourtDto(court.Id, court.Name, court.Sport, court.IsCovered, court.SortOrder, court.IsActive, 0);
    }

    /// <inheritdoc />
    public async Task<CourtDto> UpdateCourtAsync(Guid clubId, Guid courtId, SaveCourtRequest r, CancellationToken ct = default)
    {
        var court = await FindCourtAsync(clubId, courtId, ct);

        if (court.IsActive && !r.IsActive)
        {
            var now = time.GetUtcNow().UtcDateTime;
            var upcoming = await db.Bookings.CountAsync(
                b => b.CourtId == courtId && b.Status == BookingStatus.Confirmed && b.EndsAt > now, ct);
            if (upcoming > 0)
            {
                throw new AppException(
                    $"La cancha tiene {upcoming} reserva(s) por delante. Cancelalas o movelas antes de desactivarla.",
                    StatusCodes.Status409Conflict, "court_has_bookings");
            }
        }

        court.Name = r.Name.Trim();
        court.Sport = r.Sport;
        court.IsCovered = r.IsCovered;
        court.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        var slotCount = await db.SlotTemplates.CountAsync(s => s.CourtId == courtId, ct);
        return new CourtDto(court.Id, court.Name, court.Sport, court.IsCovered, court.SortOrder, court.IsActive, slotCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CourtDto>> ReorderCourtsAsync(Guid clubId, IReadOnlyList<Guid> courtIds, CancellationToken ct = default)
    {
        var courts = await db.Courts.Where(c => c.ClubId == clubId).ToListAsync(ct);
        if (courtIds.Count != courts.Count || courtIds.Distinct().Count() != courts.Count || courtIds.Any(id => courts.All(c => c.Id != id)))
        {
            throw new AppException("El orden tiene que incluir todas las canchas del club, una vez cada una.",
                StatusCodes.Status400BadRequest, "invalid_order");
        }
        for (var i = 0; i < courtIds.Count; i++)
        {
            courts.Single(c => c.Id == courtIds[i]).SortOrder = i + 1;
        }
        await db.SaveChangesAsync(ct);
        return await GetCourtsAsync(clubId, ct);
    }

    // ---- Grilla ----

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlotTemplateDto>> GetSlotsAsync(Guid clubId, Guid courtId, CancellationToken ct = default)
    {
        await FindCourtAsync(clubId, courtId, ct);
        return await SlotsOfAsync(courtId, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlotTemplateDto>> ReplaceSlotsAsync(
        Guid clubId, Guid courtId, IReadOnlyList<SlotInput> slots, CancellationToken ct = default)
    {
        await FindCourtAsync(clubId, courtId, ct);
        EnsureNoOverlap(slots);
        await SaveGridAsync(courtId, slots, ct);
        return await SlotsOfAsync(courtId, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlotTemplateDto>> GenerateSlotsAsync(
        Guid clubId, Guid courtId, GenerateSlotsRequest r, CancellationToken ct = default)
    {
        await FindCourtAsync(clubId, courtId, ct);

        // Minutos desde la apertura: así "desde las 17" sigue valiendo para un turno de 00:30 (después de medianoche).
        var open = r.FirstStart.Hour * 60 + r.FirstStart.Minute;
        int SinceOpen(TimeOnly t) => ((t.Hour * 60 + t.Minute) - open + DayMinutes) % DayMinutes;
        decimal PriceFor(TimeOnly start) =>
            r.PeakFrom is { } peak && r.PeakPrice is { } peakPrice && SinceOpen(start) >= SinceOpen(peak) ? peakPrice : r.Price;

        var generated = SlotGridGenerator.Generate(r.FirstStart, r.LastStart, r.DurationMinutes, r.Days, PriceFor, r.EveryMinutes)
            .Select(s => new SlotInput(s.DayOfWeek, s.StartTime, s.DurationMinutes, s.Price))
            .ToList();

        // Se conservan los turnos de los otros días, salvo los que se pisarían con los nuevos.
        var kept = (await SlotsOfAsync(courtId, ct))
            .Where(s => !r.Days.Contains(s.DayOfWeek))
            .Select(s => new SlotInput(s.DayOfWeek, s.StartTime, s.DurationMinutes, s.Price))
            .Where(s => !generated.Any(g => Overlaps(g, s)))
            .ToList();

        var grid = kept.Concat(generated).ToList();
        EnsureNoOverlap(grid);
        await SaveGridAsync(courtId, grid, ct);
        return await SlotsOfAsync(courtId, ct);
    }

    /// <inheritdoc />
    public async Task CopySlotsAsync(Guid clubId, Guid sourceCourtId, IReadOnlyList<Guid> targetCourtIds, CancellationToken ct = default)
    {
        await FindCourtAsync(clubId, sourceCourtId, ct);
        var targets = targetCourtIds.Distinct().Where(id => id != sourceCourtId).ToList();
        var validTargets = await db.Courts.CountAsync(c => c.ClubId == clubId && targets.Contains(c.Id), ct);
        if (targets.Count == 0 || validTargets != targets.Count)
        {
            throw new AppException("Elegí canchas del club para copiar la grilla.", StatusCodes.Status400BadRequest, "invalid_targets");
        }

        var grid = (await SlotsOfAsync(sourceCourtId, ct))
            .Select(s => new SlotInput(s.DayOfWeek, s.StartTime, s.DurationMinutes, s.Price))
            .ToList();
        foreach (var target in targets)
        {
            await SaveGridAsync(target, grid, ct);
        }
    }

    // ---- Bloqueos ----

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlockDto>> GetBlocksAsync(Guid clubId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var zone = await ZoneOfAsync(clubId, ct);
        var today = DateOnly.FromDateTime(ClubTime.ToLocal(time.GetUtcNow().UtcDateTime, zone));
        var start = ClubTime.ToUtc(from ?? today, TimeOnly.MinValue, zone);
        var end = ClubTime.ToUtc((to ?? today.AddDays(DefaultBlocksDays)).AddDays(1), TimeOnly.MinValue, zone);

        var blocks = await db.Blocks.AsNoTracking()
            .Include(b => b.Court)
            .Where(b => b.Court.ClubId == clubId && b.StartsAt < end && b.EndsAt > start)
            .OrderBy(b => b.StartsAt).ThenBy(b => b.Court.SortOrder)
            .ToListAsync(ct);
        return blocks.Select(b => ToDto(b, zone)).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlockDto>> CreateBlockAsync(Guid clubId, CreateBlockRequest r, CancellationToken ct = default)
    {
        var zone = await ZoneOfAsync(clubId, ct);
        var courtIds = r.CourtIds.Distinct().ToList();
        var courts = await db.Courts.Where(c => c.ClubId == clubId && courtIds.Contains(c.Id)).ToListAsync(ct);
        if (courts.Count != courtIds.Count) throw AppException.NotFound("La cancha no existe.");

        var startsAt = ClubTime.ToUtc(r.StartDate, r.StartTime, zone);
        var endsAt = ClubTime.ToUtc(r.EndDate, r.EndTime, zone);

        var clashing = await db.Bookings.AsNoTracking()
            .Include(b => b.Court).Include(b => b.Customer)
            .Where(b => courtIds.Contains(b.CourtId) && b.Status != BookingStatus.Cancelled
                && b.StartsAt < endsAt && b.EndsAt > startsAt)
            .OrderBy(b => b.StartsAt)
            .ToListAsync(ct);
        if (clashing.Count > 0)
        {
            throw AppException.Conflict(
                $"Hay {clashing.Count} reserva(s) en ese horario. Cancelalas (y avisale a los clientes) antes de bloquear.",
                new BlockConflicts(clashing.Select(b => BookingService.ToDto(b, zone)).ToList()));
        }

        var blocks = courts.Select(c => new Block
        {
            CourtId = c.Id, Court = c, StartsAt = startsAt, EndsAt = endsAt, Reason = r.Reason.Trim(),
        }).ToList();
        db.Blocks.AddRange(blocks);
        await db.SaveChangesAsync(ct);
        return blocks.OrderBy(b => b.Court.SortOrder).Select(b => ToDto(b, zone)).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteBlockAsync(Guid clubId, Guid blockId, CancellationToken ct = default)
    {
        var deleted = await db.Blocks.Where(b => b.Id == blockId && b.Court.ClubId == clubId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw AppException.NotFound("El bloqueo no existe.");
    }

    // ---- Helpers ----

    private async Task<Court> FindCourtAsync(Guid clubId, Guid courtId, CancellationToken ct) =>
        await db.Courts.FirstOrDefaultAsync(c => c.Id == courtId && c.ClubId == clubId, ct)
        ?? throw AppException.NotFound("La cancha no existe.");

    private async Task<TimeZoneInfo> ZoneOfAsync(Guid clubId, CancellationToken ct) =>
        ClubTime.Zone(await db.Clubs.Where(c => c.Id == clubId).Select(c => c.TimeZone).FirstOrDefaultAsync(ct)
            ?? throw AppException.NotFound("El club no existe."));

    private async Task<IReadOnlyList<SlotTemplateDto>> SlotsOfAsync(Guid courtId, CancellationToken ct) =>
        (await db.SlotTemplates.AsNoTracking().Where(s => s.CourtId == courtId).ToListAsync(ct))
            .OrderBy(s => ((int)s.DayOfWeek + 6) % 7).ThenBy(s => s.StartTime)   // lunes primero
            .Select(s => new SlotTemplateDto(s.Id, s.DayOfWeek, s.StartTime, s.DurationMinutes, s.Price))
            .ToList();

    private async Task SaveGridAsync(Guid courtId, IReadOnlyList<SlotInput> slots, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SlotTemplates.Where(s => s.CourtId == courtId).ExecuteDeleteAsync(ct);
        db.SlotTemplates.AddRange(slots.Select(s => new SlotTemplate
        {
            CourtId = courtId, DayOfWeek = s.DayOfWeek, StartTime = s.StartTime, DurationMinutes = s.DurationMinutes, Price = s.Price,
        }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>Inicio del turno en minutos desde el lunes 00:00.</summary>
    private static int WeekMinute(SlotInput s) => (((int)s.DayOfWeek + 6) % 7) * DayMinutes + s.StartTime.Hour * 60 + s.StartTime.Minute;

    /// <summary>Superposición en la semana, contemplando el turno del domingo que cruza al lunes.</summary>
    private static bool Overlaps(SlotInput a, SlotInput b)
    {
        int aStart = WeekMinute(a), bStart = WeekMinute(b);
        foreach (var shift in new[] { 0, WeekMinutes, -WeekMinutes })
        {
            var bs = bStart + shift;
            if (aStart < bs + b.DurationMinutes && bs < aStart + a.DurationMinutes) return true;
        }
        return false;
    }

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    private static void EnsureNoOverlap(IReadOnlyList<SlotInput> slots)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            for (var j = i + 1; j < slots.Count; j++)
            {
                if (!Overlaps(slots[i], slots[j])) continue;
                var a = slots[i];
                var b = slots[j];
                throw new AppException(
                    $"Se superponen los turnos del {Es.DateTimeFormat.GetDayName(a.DayOfWeek)} {a.StartTime:HH\\:mm} " +
                    $"({a.DurationMinutes} min) y del {Es.DateTimeFormat.GetDayName(b.DayOfWeek)} {b.StartTime:HH\\:mm}.",
                    StatusCodes.Status400BadRequest, "slot_overlap");
            }
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ClubSettingsDto ToDto(Club c) => new(
        c.Id, c.Name, c.Address, c.Phone, c.TimeZone, c.AssistantName, c.BotInstructions, c.BotShowsPrices,
        c.CancellationMinHours, c.MinLeadMinutes, c.BookingHorizonDays, c.MaxActiveBookingsPerCustomer,
        c.ChatwootAccountId, c.ChatwootInboxId);

    private static BlockDto ToDto(Block b, TimeZoneInfo zone)
    {
        var start = ClubTime.ToLocal(b.StartsAt, zone);
        var end = ClubTime.ToLocal(b.EndsAt, zone);
        return new BlockDto(b.Id, b.CourtId, b.Court.Name, b.StartsAt, b.EndsAt,
            DateOnly.FromDateTime(start), TimeOnly.FromDateTime(start), DateOnly.FromDateTime(end), TimeOnly.FromDateTime(end), b.Reason);
    }
}
