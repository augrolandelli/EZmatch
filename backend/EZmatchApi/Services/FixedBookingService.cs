using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

public interface IFixedBookingService
{
    /// <summary>Turnos fijos del club (activos primero), con la próxima fecha y las que no se pudieron reservar.</summary>
    Task<IReadOnlyList<FixedBookingDto>> ListAsync(Guid clubId, CancellationToken ct = default);

    /// <summary>Crea el turno fijo y reserva enseguida las próximas semanas.</summary>
    Task<FixedBookingDto> CreateAsync(Guid clubId, CreateFixedBookingRequest request, CancellationToken ct = default);

    /// <summary>Da de baja el turno fijo y cancela sus reservas futuras.</summary>
    Task<FixedBookingDto> EndAsync(Guid clubId, Guid id, CancellationToken ct = default);

    /// <summary>Genera las reservas que falten de los turnos fijos activos (de un club o de todos).</summary>
    Task<int> MaterializeAsync(Guid? clubId = null, CancellationToken ct = default);
}

/// <summary>
/// Turnos fijos semanales. Cada uno se materializa en reservas reales hasta <see cref="HorizonDays"/> días
/// (o el horizonte del bot, si es mayor). Si una fecha ya tuvo su reserva —aunque se haya cancelado— no se
/// vuelve a crear: cancelar un martes suelto libera solo ese martes.
/// </summary>
public class FixedBookingService(
    EZmatchDbContext db,
    IBookingService bookings,
    TimeProvider time,
    ILogger<FixedBookingService> logger) : IFixedBookingService
{
    public const int HorizonDays = 35;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FixedBookingDto>> ListAsync(Guid clubId, CancellationToken ct = default)
    {
        var club = await GetClubAsync(clubId, ct);
        var list = await db.FixedBookings.AsNoTracking()
            .Include(f => f.Court).ThenInclude(c => c.SlotTemplates)
            .Include(f => f.Customer)
            .Where(f => f.ClubId == clubId)
            .OrderByDescending(f => f.IsActive).ThenBy(f => f.DayOfWeek).ThenBy(f => f.StartTime)
            .ToListAsync(ct);
        var result = new List<FixedBookingDto>();
        foreach (var f in list) result.Add(await ToDtoAsync(f, club, ct));
        return result;
    }

    /// <inheritdoc />
    public async Task<FixedBookingDto> CreateAsync(Guid clubId, CreateFixedBookingRequest request, CancellationToken ct = default)
    {
        var club = await GetClubAsync(clubId, ct);
        var today = Today(club);
        var court = await db.Courts
            .Include(c => c.SlotTemplates)
            .FirstOrDefaultAsync(c => c.Id == request.CourtId && c.ClubId == clubId && c.IsActive, ct)
            ?? throw AppException.NotFound("La cancha no existe.");

        if (!court.SlotTemplates.Any(t => t.DayOfWeek == request.DayOfWeek && t.StartTime == request.StartTime))
        {
            throw new AppException("Ese día y horario no existe en la grilla de la cancha.",
                StatusCodes.Status400BadRequest, "slot_not_in_grid");
        }

        if (await db.FixedBookings.AnyAsync(f => f.CourtId == court.Id && f.IsActive
                && f.DayOfWeek == request.DayOfWeek && f.StartTime == request.StartTime, ct))
        {
            throw new AppException("Esa cancha ya tiene un turno fijo ese día y horario.",
                StatusCodes.Status409Conflict, "fixed_booking_exists");
        }

        var phone = PhoneNumber.NormalizeArgentine(request.Phone);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.ClubId == clubId && c.Phone == phone, ct);
        if (customer is null)
        {
            var name = request.CustomerName?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                throw new AppException("Falta el nombre: ese teléfono todavía no es cliente del club.",
                    StatusCodes.Status422UnprocessableEntity, "customer_name_required");
            }
            customer = new Customer { ClubId = clubId, Phone = phone, Name = name };
            db.Customers.Add(customer);
        }

        var startsOn = request.StartsOn is { } s && s > today ? s : today;
        var fixedBooking = new FixedBooking
        {
            ClubId = clubId,
            CourtId = court.Id,
            CustomerId = customer.Id,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            StartsOn = startsOn,
            EndsOn = request.EndsOn,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        };
        db.FixedBookings.Add(fixedBooking);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Turno fijo {FixedBookingId} creado en {CourtId} ({Day} {Time})",
            fixedBooking.Id, court.Id, request.DayOfWeek, request.StartTime);

        fixedBooking.Court = court;
        fixedBooking.Customer = customer;
        await MaterializeOneAsync(fixedBooking, club, ct);
        return await ToDtoAsync(fixedBooking, club, ct);
    }

    /// <inheritdoc />
    public async Task<FixedBookingDto> EndAsync(Guid clubId, Guid id, CancellationToken ct = default)
    {
        var club = await GetClubAsync(clubId, ct);
        var fixedBooking = await db.FixedBookings
            .Include(f => f.Court).ThenInclude(c => c.SlotTemplates)
            .Include(f => f.Customer)
            .FirstOrDefaultAsync(f => f.Id == id && f.ClubId == clubId, ct)
            ?? throw AppException.NotFound("El turno fijo no existe.");

        if (fixedBooking.IsActive)
        {
            var now = time.GetUtcNow().UtcDateTime;
            fixedBooking.IsActive = false;
            fixedBooking.EndedAt = now;
            fixedBooking.EndsOn = Today(club);

            var future = await db.Bookings
                .Where(b => b.FixedBookingId == id && b.Status == BookingStatus.Confirmed && b.StartsAt > now)
                .ToListAsync(ct);
            foreach (var b in future)
            {
                b.Status = BookingStatus.Cancelled;
                b.CancelledAt = now;
                b.CancelledBy = BookingSource.Panel;
                b.CancelReason = "Turno fijo dado de baja";
            }
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Turno fijo {FixedBookingId} dado de baja ({Count} reservas canceladas)", id, future.Count);
        }
        return await ToDtoAsync(fixedBooking, club, ct);
    }

    /// <inheritdoc />
    public async Task<int> MaterializeAsync(Guid? clubId = null, CancellationToken ct = default)
    {
        var active = await db.FixedBookings
            .Include(f => f.Court)
            .Include(f => f.Customer)
            .Where(f => f.IsActive && (clubId == null || f.ClubId == clubId))
            .ToListAsync(ct);
        var clubIds = active.Select(f => f.ClubId).Distinct().ToList();
        var clubs = await db.Clubs.AsNoTracking()
            .Where(c => clubIds.Contains(c.Id) && c.IsActive)
            .ToDictionaryAsync(c => c.Id, ct);

        var created = 0;
        foreach (var f in active)
        {
            if (clubs.TryGetValue(f.ClubId, out var club)) created += await MaterializeOneAsync(f, club, ct);
        }
        return created;
    }

    /// <summary>Reserva las fechas que falten. Las que no se pueden (ocupada, bloqueada, fuera de grilla) se saltean.</summary>
    private async Task<int> MaterializeOneAsync(FixedBooking f, Club club, CancellationToken ct)
    {
        if (!f.Court.IsActive) return 0;
        var zone = ClubTime.Zone(club.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;
        var existing = (await db.Bookings.AsNoTracking()
            .Where(b => b.FixedBookingId == f.Id)
            .Select(b => b.StartsAt)
            .ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var date in Dates(f, club))
        {
            var startsAt = ClubTime.ToUtc(date, f.StartTime, zone);
            if (startsAt <= now || existing.Contains(startsAt)) continue;
            try
            {
                await bookings.CreateAsync(club.Id,
                    new CreateBookingRequest(f.Court.Sport, date, f.StartTime, f.Customer.Phone, f.Customer.Name, f.CourtId, f.Id),
                    BookingSource.Panel, ct);
                created++;
            }
            catch (AppException ex) when (ex.Code is "conflict" or "slot_not_in_grid")
            {
                // Queda como fecha faltante; se reintenta en la próxima pasada por si se libera.
            }
        }
        if (created > 0) logger.LogInformation("Turno fijo {FixedBookingId}: {Count} reservas generadas", f.Id, created);
        return created;
    }

    /// <summary>Fechas del turno fijo desde hoy (o su inicio) hasta el horizonte.</summary>
    private IEnumerable<DateOnly> Dates(FixedBooking f, Club club)
    {
        var today = Today(club);
        var from = f.StartsOn > today ? f.StartsOn : today;
        var until = today.AddDays(Math.Max(HorizonDays, club.BookingHorizonDays));
        if (f.EndsOn is { } end && end < until) until = end;
        var first = from.AddDays(((int)f.DayOfWeek - (int)from.DayOfWeek + 7) % 7);
        for (var d = first; d <= until; d = d.AddDays(7)) yield return d;
    }

    private async Task<FixedBookingDto> ToDtoAsync(FixedBooking f, Club club, CancellationToken ct)
    {
        var zone = ClubTime.Zone(club.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;
        var linked = await db.Bookings.AsNoTracking()
            .Where(b => b.FixedBookingId == f.Id && b.StartsAt > now)
            .Select(b => new { b.StartsAt, b.Status })
            .ToListAsync(ct);
        var linkedStarts = linked.Select(b => b.StartsAt).ToHashSet();

        var nextStart = linked.Where(b => b.Status == BookingStatus.Confirmed).Select(b => (DateTime?)b.StartsAt).Min();
        DateOnly? next = nextStart is { } n ? DateOnly.FromDateTime(ClubTime.ToLocal(n, zone)) : null;
        var missing = f.IsActive
            ? Dates(f, club).Where(d =>
            {
                var startsAt = ClubTime.ToUtc(d, f.StartTime, zone);
                return startsAt > now && !linkedStarts.Contains(startsAt);
            }).ToList()
            : [];

        var duration = f.Court.SlotTemplates
            .FirstOrDefault(t => t.DayOfWeek == f.DayOfWeek && t.StartTime == f.StartTime)?.DurationMinutes;
        return new FixedBookingDto(
            f.Id, f.CourtId, f.Court.Name, f.Court.Sport, f.DayOfWeek, f.StartTime,
            duration is { } m ? f.StartTime.AddMinutes(m) : null,
            f.CustomerId, f.Customer.Name, f.Customer.Phone, f.StartsOn, f.EndsOn, f.Notes, f.IsActive, next, missing);
    }

    private DateOnly Today(Club club) =>
        DateOnly.FromDateTime(ClubTime.ToLocal(time.GetUtcNow().UtcDateTime, ClubTime.Zone(club.TimeZone)));

    private async Task<Club> GetClubAsync(Guid clubId, CancellationToken ct) =>
        await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clubId, ct)
        ?? throw AppException.NotFound("El club no existe.");
}

/// <summary>Cada 6 horas genera las reservas de los turnos fijos que van entrando en el horizonte.</summary>
public class FixedBookingWorker(IServiceScopeFactory scopes, ILogger<FixedBookingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var created = await scope.ServiceProvider.GetRequiredService<IFixedBookingService>().MaterializeAsync(ct: stoppingToken);
                if (created > 0) logger.LogInformation("Turnos fijos: {Count} reservas generadas", created);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error generando reservas de turnos fijos");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
