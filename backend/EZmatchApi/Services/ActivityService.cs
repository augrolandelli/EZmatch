using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

public enum ActivityKind
{
    /// <summary>El bot tomó una reserva.</summary>
    Booked,
    /// <summary>Alguien canceló por WhatsApp.</summary>
    Cancelled,
}

/// <param name="At">Cuándo pasó (UTC).</param>
public record ActivityDto(
    string Id,
    ActivityKind Kind,
    DateTime At,
    Guid BookingId,
    string CustomerName,
    string CourtName,
    DateOnly Date,
    TimeOnly StartTime);

public interface IActivityService
{
    /// <summary>Lo último que hizo el bot (reservas y cancelaciones por WhatsApp), más reciente primero.</summary>
    Task<IReadOnlyList<ActivityDto>> GetRecentAsync(Guid clubId, CancellationToken ct = default);
}

/// <summary>Avisos del panel: qué reservó o canceló la gente por WhatsApp en los últimos días.</summary>
public class ActivityService(EZmatchDbContext db, TimeProvider time) : IActivityService
{
    private const int Limit = 30;
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActivityDto>> GetRecentAsync(Guid clubId, CancellationToken ct = default)
    {
        var club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clubId, ct)
            ?? throw AppException.NotFound("El club no existe.");
        var zone = ClubTime.Zone(club.TimeZone);
        var since = time.GetUtcNow().UtcDateTime - Window;

        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.ClubId == clubId
                && ((b.Source == BookingSource.WhatsApp && b.CreatedAt >= since)
                    || (b.CancelledBy == BookingSource.WhatsApp && b.CancelledAt >= since)))
            .Select(b => new
            {
                b.Id, b.Source, b.CreatedAt, b.CancelledBy, b.CancelledAt, b.StartsAt,
                CustomerName = b.Customer.Name, CourtName = b.Court.Name,
            })
            .ToListAsync(ct);

        var events = new List<ActivityDto>();
        foreach (var b in rows)
        {
            var local = ClubTime.ToLocal(b.StartsAt, zone);
            ActivityDto Event(ActivityKind kind, DateTime at) => new(
                $"{b.Id}:{kind}", kind, at, b.Id, b.CustomerName, b.CourtName,
                DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));

            if (b.Source == BookingSource.WhatsApp && b.CreatedAt >= since) events.Add(Event(ActivityKind.Booked, b.CreatedAt));
            if (b.CancelledBy == BookingSource.WhatsApp && b.CancelledAt is { } at && at >= since) events.Add(Event(ActivityKind.Cancelled, at));
        }
        // A igual hora, la cancelación va primero: siempre es posterior a la reserva.
        return events.OrderByDescending(e => e.At).ThenByDescending(e => e.Kind).Take(Limit).ToList();
    }
}
