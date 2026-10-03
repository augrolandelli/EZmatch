using EZmatchApi.Data;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

/// <summary>Ventana en la que el bot acepta reservas: anticipación mínima y horizonte máximo.</summary>
public readonly record struct BookingWindow(DateTime EarliestStart, DateTime LatestStart)
{
    public static BookingWindow For(Club club, DateTime utcNow) =>
        new(utcNow.AddMinutes(club.MinLeadMinutes), utcNow.AddDays(club.BookingHorizonDays));

    public bool Contains(DateTime startsAt) => startsAt >= EarliestStart && startsAt <= LatestStart;
}

/// <summary>Ocupación de canchas: reservas no canceladas y bloqueos.</summary>
public static class Occupancy
{
    /// <summary>Filtra los turnos que se superponen con una reserva activa o un bloqueo de su cancha.</summary>
    public static async Task<List<SlotInstance>> ExcludeBusyAsync(
        EZmatchDbContext db, IReadOnlyCollection<SlotInstance> slots, CancellationToken ct)
    {
        if (slots.Count == 0) return [];

        var courtIds = slots.Select(s => s.CourtId).Distinct().ToList();
        var from = slots.Min(s => s.StartsAt);
        var to = slots.Max(s => s.EndsAt);

        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => courtIds.Contains(b.CourtId)
                && b.Status != BookingStatus.Cancelled
                && b.StartsAt < to && b.EndsAt > from)
            .Select(b => new { b.CourtId, b.StartsAt, b.EndsAt })
            .ToListAsync(ct);

        var blocks = await db.Blocks.AsNoTracking()
            .Where(b => courtIds.Contains(b.CourtId) && b.StartsAt < to && b.EndsAt > from)
            .Select(b => new { b.CourtId, b.StartsAt, b.EndsAt })
            .ToListAsync(ct);

        var busy = bookings.Concat(blocks).ToLookup(b => b.CourtId);
        return slots
            .Where(s => !busy[s.CourtId].Any(b => s.Overlaps(b.StartsAt, b.EndsAt)))
            .ToList();
    }
}
