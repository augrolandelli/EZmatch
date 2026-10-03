using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EZmatchApi.Services;

public interface IBookingService
{
    /// <summary>
    /// Reserva un turno de la grilla. Asigna la primera cancha libre (o la pedida).
    /// Las reglas de bot (anticipación, horizonte, bloqueo, límite) solo aplican a <see cref="BookingSource.WhatsApp"/>.
    /// </summary>
    Task<BookingDto> CreateAsync(Guid clubId, CreateBookingRequest request, BookingSource source, CancellationToken ct = default);

    /// <summary>Reservas confirmadas que todavía no terminaron, de un teléfono.</summary>
    Task<IReadOnlyList<BookingDto>> GetUpcomingForPhoneAsync(Guid clubId, string phone, CancellationToken ct = default);

    /// <summary>
    /// Cancela una reserva. Desde WhatsApp solo el mismo teléfono y con la anticipación mínima del club.
    /// </summary>
    Task<BookingDto> CancelAsync(Guid clubId, Guid bookingId, string? phone, BookingSource source, string? reason = null, CancellationToken ct = default);
}

/// <summary>Creación y cancelación de reservas (spec §3.3–§3.5).</summary>
public class BookingService(
    EZmatchDbContext db,
    IAvailabilityService availability,
    TimeProvider time,
    ILogger<BookingService> logger) : IBookingService
{
    private const int AlternativesCount = 3;

    /// <summary>Clave de advisory lock (bigint) derivada del id de la cancha.</summary>
    private static long CourtLockKey(Guid courtId)
    {
        Span<byte> bytes = stackalloc byte[16];
        courtId.TryWriteBytes(bytes);
        return BitConverter.ToInt64(bytes[..8]) ^ BitConverter.ToInt64(bytes[8..]);
    }

    /// <summary>SQLSTATE de Postgres dentro de la cadena de excepciones (EF puede envolverla dos veces).</summary>
    private static string? SqlState(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg) return pg.SqlState;
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<BookingDto> CreateAsync(Guid clubId, CreateBookingRequest request, BookingSource source, CancellationToken ct = default)
    {
        var phone = PhoneNumber.Normalize(request.Phone);
        var club = await GetClubAsync(clubId, ct);
        var zone = ClubTime.Zone(club.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;
        var fromBot = source == BookingSource.WhatsApp;

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.ClubId == clubId && c.Phone == phone, ct);
        if (customer is null)
        {
            var name = request.CustomerName?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                throw new AppException("Falta el nombre de la persona para registrar la reserva.",
                    StatusCodes.Status422UnprocessableEntity, "customer_name_required");
            }
            customer = new Customer { ClubId = clubId, Phone = phone, Name = name };
            db.Customers.Add(customer);
        }
        else if (fromBot)
        {
            if (customer.IsBlocked)
            {
                throw AppException.Forbidden("Este número no puede reservar por WhatsApp; tiene que comunicarse con el club.", "customer_blocked");
            }
            var active = await db.Bookings.CountAsync(
                b => b.CustomerId == customer.Id && b.Status == BookingStatus.Confirmed && b.EndsAt > now, ct);
            if (active >= club.MaxActiveBookingsPerCustomer)
            {
                throw AppException.Forbidden(
                    $"Ya tiene {active} reservas activas, que es el máximo permitido por WhatsApp.", "max_active_bookings");
            }
        }

        var courts = await db.Courts.AsNoTracking()
            .Include(c => c.SlotTemplates)
            .Where(c => c.ClubId == clubId && c.IsActive && c.Sport == request.Sport
                && (request.CourtId == null || c.Id == request.CourtId))
            .OrderBy(c => c.SortOrder)
            .ToListAsync(ct);

        var candidates = courts
            .SelectMany(c => SlotCalculator.ForDate(c, request.Date, zone))
            .Where(s => s.StartTime == request.StartTime)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new AppException("Ese horario no existe en la grilla del club.",
                StatusCodes.Status400BadRequest, "slot_not_in_grid")
            {
                Details = new BookingAlternatives(await AlternativesAsync(clubId, request, zone, fromBot, ct)),
            };
        }

        if (fromBot)
        {
            var window = BookingWindow.For(club, now);
            var startsAt = candidates[0].StartsAt;
            if (startsAt < window.EarliestStart)
            {
                throw new AppException(
                    $"Por WhatsApp se reserva con al menos {club.MinLeadMinutes} minutos de anticipación.",
                    StatusCodes.Status400BadRequest, "too_soon");
            }
            if (startsAt > window.LatestStart)
            {
                throw new AppException(
                    $"Solo se puede reservar con hasta {club.BookingHorizonDays} días de anticipación.",
                    StatusCodes.Status400BadRequest, "too_far");
            }
        }

        // La verificación previa evita intentos inútiles; la garantía real es el constraint de exclusión.
        foreach (var slot in await Occupancy.ExcludeBusyAsync(db, candidates, ct))
        {
            var booking = new Booking
            {
                ClubId = clubId,
                CourtId = slot.CourtId,
                CustomerId = customer.Id,
                StartsAt = slot.StartsAt,
                EndsAt = slot.EndsAt,
                Price = slot.Price,
                Source = source,
            };
            db.Bookings.Add(booking);

            try
            {
                // Inserciones concurrentes sobre la misma cancha se esperan mutuamente en el chequeo de
                // exclusión y Postgres aborta alguna por deadlock. Un lock por cancha las encola: quien
                // llega segundo ve la reserva ya confirmada y recibe una violación de exclusión limpia.
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({CourtLockKey(slot.CourtId)})", ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                logger.LogInformation("Reserva {BookingId} creada en {CourtId} para {StartsAt} ({Source})",
                    booking.Id, slot.CourtId, slot.StartsAt, source);
                return ToDto(booking, slot.CourtName, slot.Sport, customer, zone);
            }
            catch (Exception ex) when (SqlState(ex) is PostgresErrorCodes.ExclusionViolation)
            {
                // Otra reserva ganó la carrera por esta cancha: probar con la siguiente.
                db.Entry(booking).State = EntityState.Detached;
            }
        }

        throw AppException.Conflict("Ese turno ya está ocupado.",
            new BookingAlternatives(await AlternativesAsync(clubId, request, zone, fromBot, ct)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BookingDto>> GetUpcomingForPhoneAsync(Guid clubId, string phone, CancellationToken ct = default)
    {
        var normalized = PhoneNumber.Normalize(phone);
        var club = await GetClubAsync(clubId, ct);
        var zone = ClubTime.Zone(club.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;

        var bookings = await db.Bookings.AsNoTracking()
            .Include(b => b.Court)
            .Include(b => b.Customer)
            .Where(b => b.ClubId == clubId && b.Customer.Phone == normalized
                && b.Status == BookingStatus.Confirmed && b.EndsAt > now)
            .OrderBy(b => b.StartsAt)
            .ToListAsync(ct);

        return bookings.Select(b => ToDto(b, b.Court.Name, b.Court.Sport, b.Customer, zone)).ToList();
    }

    /// <inheritdoc />
    public async Task<BookingDto> CancelAsync(Guid clubId, Guid bookingId, string? phone, BookingSource source, string? reason = null, CancellationToken ct = default)
    {
        var fromBot = source == BookingSource.WhatsApp;
        var normalizedPhone = fromBot ? PhoneNumber.Normalize(phone) : null;
        var club = await GetClubAsync(clubId, ct);
        var zone = ClubTime.Zone(club.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;

        var booking = await db.Bookings
            .Include(b => b.Court)
            .Include(b => b.Customer)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.ClubId == clubId, ct);

        // Por WhatsApp, una reserva ajena se trata como inexistente (no revelar datos de otros).
        if (booking is null || (fromBot && booking.Customer.Phone != normalizedPhone))
        {
            throw AppException.NotFound("No se encontró esa reserva.");
        }

        if (booking.Status != BookingStatus.Confirmed)
        {
            throw new AppException("La reserva ya no está activa.", StatusCodes.Status409Conflict, "booking_not_active");
        }

        if (fromBot && booking.StartsAt - now < TimeSpan.FromHours(club.CancellationMinHours))
        {
            throw AppException.Forbidden(
                $"Por WhatsApp solo se puede cancelar con al menos {club.CancellationMinHours} horas de anticipación.",
                "cancellation_too_late");
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = now;
        booking.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Reserva {BookingId} cancelada ({Source})", booking.Id, source);
        return ToDto(booking, booking.Court.Name, booking.Court.Sport, booking.Customer, zone);
    }

    private async Task<Club> GetClubAsync(Guid clubId, CancellationToken ct) =>
        await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clubId && c.IsActive, ct)
        ?? throw AppException.NotFound("El club no existe.");

    /// <summary>Los turnos libres más cercanos al pedido, para que el bot ofrezca opciones.</summary>
    private async Task<IReadOnlyList<AvailableSlotDto>> AlternativesAsync(
        Guid clubId, CreateBookingRequest request, TimeZoneInfo zone, bool enforceBookingWindow, CancellationToken ct)
    {
        var requested = ClubTime.ToUtc(request.Date, request.StartTime, zone);
        var free = await availability.GetAvailableAsync(
            clubId, new AvailabilityQuery(request.Date, request.Sport), enforceBookingWindow, ct);

        return free
            .OrderBy(s => Math.Abs((s.StartsAt - requested).Ticks))
            .Take(AlternativesCount)
            .OrderBy(s => s.StartsAt)
            .ToList();
    }

    private static BookingDto ToDto(Booking b, string courtName, Sport sport, Customer customer, TimeZoneInfo zone)
    {
        var localStart = ClubTime.ToLocal(b.StartsAt, zone);
        var localEnd = ClubTime.ToLocal(b.EndsAt, zone);
        return new BookingDto(
            b.Id, b.CourtId, courtName, sport,
            DateOnly.FromDateTime(localStart), TimeOnly.FromDateTime(localStart), TimeOnly.FromDateTime(localEnd),
            b.StartsAt, b.EndsAt, b.Price, b.Status, b.PaymentStatus, b.Source,
            customer.Name, customer.Phone);
    }
}
