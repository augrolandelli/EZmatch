using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

public interface IAvailabilityService
{
    /// <summary>Turnos libres de una fecha, agrupados por deporte y horario, ordenados por hora.</summary>
    /// <param name="enforceBookingWindow">
    /// true para el bot: excluye turnos fuera de la anticipación mínima y del horizonte del club.
    /// </param>
    Task<IReadOnlyList<AvailableSlotDto>> GetAvailableAsync(
        Guid clubId, AvailabilityQuery query, bool enforceBookingWindow, CancellationToken ct = default);
}

/// <summary>Cálculo de disponibilidad: grilla del día − reservas activas − bloqueos (spec §3.2).</summary>
public class AvailabilityService(EZmatchDbContext db, TimeProvider time) : IAvailabilityService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableSlotDto>> GetAvailableAsync(
        Guid clubId, AvailabilityQuery query, bool enforceBookingWindow, CancellationToken ct = default)
    {
        var club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clubId && c.IsActive, ct)
            ?? throw AppException.NotFound("El club no existe.");
        var zone = ClubTime.Zone(club.TimeZone);

        var courts = await db.Courts.AsNoTracking()
            .Include(c => c.SlotTemplates)
            .Where(c => c.ClubId == clubId && c.IsActive && (query.Sport == null || c.Sport == query.Sport))
            .ToListAsync(ct);

        var slots = courts
            .SelectMany(c => SlotCalculator.ForDate(c, query.Date, zone))
            .Where(s => (query.From is null || s.StartTime >= query.From)
                     && (query.To is null || s.StartTime <= query.To));

        if (enforceBookingWindow)
        {
            var window = BookingWindow.For(club, time.GetUtcNow().UtcDateTime);
            slots = slots.Where(s => window.Contains(s.StartsAt));
        }

        var free = await Occupancy.ExcludeBusyAsync(db, slots.ToList(), ct);

        return free
            .GroupBy(s => (s.Sport, s.StartTime, s.DurationMinutes))
            .Select(g =>
            {
                var first = g.First();
                var freeCourts = g
                    .OrderBy(s => s.SortOrder)
                    .Select(s => new FreeCourtDto(s.CourtId, s.CourtName, s.IsCovered, s.Price))
                    .ToList();
                return new AvailableSlotDto(
                    first.Sport, first.Date, first.StartTime, first.StartTime.AddMinutes(first.DurationMinutes),
                    first.DurationMinutes, first.StartsAt, freeCourts.Min(c => c.Price), freeCourts);
            })
            .OrderBy(s => s.StartsAt)
            .ThenBy(s => s.Sport)
            .ToList();
    }
}
