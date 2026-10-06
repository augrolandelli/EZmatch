using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

public interface IAgendaService
{
    /// <summary>Agenda de una fecha local del club (hoy si es null).</summary>
    Task<AgendaDto> GetDayAsync(Guid clubId, DateOnly? date, CancellationToken ct = default);
}

/// <summary>
/// Arma la agenda de un día: por cancha, los turnos de la grilla (libres), las reservas que empiezan ese
/// día y los bloqueos. Las reservas se muestran aunque ya no coincidan con la grilla (si el club la cambió).
/// </summary>
public class AgendaService(EZmatchDbContext db, TimeProvider time) : IAgendaService
{
    /// <inheritdoc />
    public async Task<AgendaDto> GetDayAsync(Guid clubId, DateOnly? date, CancellationToken ct = default)
    {
        var club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clubId, ct)
            ?? throw AppException.NotFound("El club no existe.");
        var zone = ClubTime.Zone(club.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(ClubTime.ToLocal(now, zone));
        var day = date ?? today;

        var dayStart = ClubTime.ToUtc(day, TimeOnly.MinValue, zone);
        var dayEnd = ClubTime.ToUtc(day.AddDays(1), TimeOnly.MinValue, zone);

        var courts = await db.Courts.AsNoTracking()
            .Include(c => c.SlotTemplates)
            .Where(c => c.ClubId == clubId && c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);
        var courtIds = courts.Select(c => c.Id).ToList();

        var slots = courts.SelectMany(c => SlotCalculator.ForDate(c, day, zone)).ToList();
        // Rango a mirar: el día calendario más lo que se extiendan los turnos que cruzan la medianoche.
        var rangeEnd = slots.Count == 0 ? dayEnd : new[] { dayEnd, slots.Max(s => s.EndsAt) }.Max();

        var bookings = await db.Bookings.AsNoTracking()
            .Include(b => b.Customer)
            .Where(b => courtIds.Contains(b.CourtId) && b.Status != BookingStatus.Cancelled
                && b.StartsAt < rangeEnd && b.EndsAt > dayStart)
            .ToListAsync(ct);
        var blocks = await db.Blocks.AsNoTracking()
            .Where(b => courtIds.Contains(b.CourtId) && b.StartsAt < rangeEnd && b.EndsAt > dayStart)
            .ToListAsync(ct);

        int MinuteOf(DateTime utc) => (int)Math.Round((ClubTime.ToLocal(utc, zone) - day.ToDateTime(TimeOnly.MinValue)).TotalMinutes);

        AgendaItemDto Item(AgendaItemKind kind, DateTime startsAt, DateTime endsAt, decimal? price,
            AgendaBookingDto? booking = null, string? blockReason = null) =>
            new(kind,
                TimeOnly.FromDateTime(ClubTime.ToLocal(startsAt, zone)),
                TimeOnly.FromDateTime(ClubTime.ToLocal(endsAt, zone)),
                MinuteOf(startsAt),
                (int)Math.Round((endsAt - startsAt).TotalMinutes),
                startsAt, endsAt, price, endsAt <= now, booking, blockReason);

        var courtDtos = new List<AgendaCourtDto>();
        foreach (var court in courts)
        {
            var courtBookings = bookings.Where(b => b.CourtId == court.Id).ToList();
            var courtBlocks = blocks.Where(b => b.CourtId == court.Id).ToList();
            var items = new List<AgendaItemDto>();

            // Reservas que empiezan este día: se dibujan con sus horarios reales.
            foreach (var b in courtBookings.Where(b => b.StartsAt >= dayStart && b.StartsAt < dayEnd))
            {
                items.Add(Item(AgendaItemKind.Booking, b.StartsAt, b.EndsAt, b.Price, new AgendaBookingDto(
                    b.Id, b.CustomerId, b.Customer.Name, b.Customer.Phone, b.Customer.IsBlocked,
                    b.Price, b.Status, b.PaymentStatus, b.Source, b.CreatedAt)));
            }

            // Bloqueos, recortados al día.
            foreach (var block in courtBlocks)
            {
                var start = block.StartsAt < dayStart ? dayStart : block.StartsAt;
                var end = block.EndsAt > rangeEnd ? rangeEnd : block.EndsAt;
                items.Add(Item(AgendaItemKind.Block, start, end, null, blockReason: block.Reason));
            }

            // Turnos de la grilla que no quedaron tapados por una reserva o un bloqueo de este día.
            foreach (var slot in slots.Where(s => s.CourtId == court.Id))
            {
                if (courtBlocks.Any(b => slot.Overlaps(b.StartsAt, b.EndsAt))) continue;
                var overlapping = courtBookings.Where(b => slot.Overlaps(b.StartsAt, b.EndsAt)).ToList();
                if (overlapping.Count == 0)
                {
                    items.Add(Item(AgendaItemKind.Free, slot.StartsAt, slot.EndsAt, slot.Price));
                }
                else if (overlapping.All(b => b.StartsAt < dayStart))
                {
                    // Lo ocupa una reserva del día anterior que cruzó la medianoche (no se dibuja en este día).
                    items.Add(Item(AgendaItemKind.Busy, slot.StartsAt, slot.EndsAt, slot.Price));
                }
            }

            courtDtos.Add(new AgendaCourtDto(court.Id, court.Name, court.Sport, court.IsCovered,
                items.OrderBy(i => i.StartMinute).ThenBy(i => i.Kind).ToList()));
        }

        var dayBookings = courtDtos.SelectMany(c => c.Items).Where(i => i.Booking is not null).Select(i => i.Booking!).ToList();
        var summary = new AgendaSummaryDto(
            Bookings: dayBookings.Count,
            FreeSlots: courtDtos.SelectMany(c => c.Items).Count(i => i.Kind == AgendaItemKind.Free && !i.IsPast),
            NoShows: dayBookings.Count(b => b.Status == BookingStatus.NoShow),
            PaidAmount: dayBookings.Where(b => b.PaymentStatus == PaymentStatus.Paid).Sum(b => b.Price),
            PendingAmount: dayBookings.Where(b => b.PaymentStatus == PaymentStatus.Unpaid && b.Status != BookingStatus.NoShow).Sum(b => b.Price));

        return new AgendaDto(day, today, courtDtos, summary);
    }
}
