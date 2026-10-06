using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

/// <summary>Un indicador del período y su valor en el período anterior (misma duración, inmediatamente antes).</summary>
public record Kpi(decimal Value, decimal Previous);

public record DashboardKpis(
    Kpi Occupancy,
    Kpi Bookings,
    Kpi PaidRevenue,
    Kpi WhatsAppShare,
    Kpi NoShowRate,
    Kpi NewCustomers);

public record NextBookingDto(TimeOnly StartTime, string CourtName, string CustomerName);

public record DashboardTodayDto(int Bookings, decimal Occupancy, decimal PaidAmount, decimal PendingAmount, NextBookingDto? Next);

public record DailyBookingsDto(DateOnly Date, int WhatsApp, int Panel);

/// <summary>Turnos de la grilla de un día de la semana a una hora, y cuántos se reservaron.</summary>
public record HeatCellDto(DayOfWeek DayOfWeek, int Hour, int Slots, int Booked);

public record CourtStatsDto(Guid Id, string Name, int Slots, int Booked, decimal Occupancy);

public record TopCustomerDto(Guid Id, string Name, string Phone, int Bookings, int NoShows);

/// <param name="Occupancy">Turnos reservados / turnos de la grilla (0 a 1). Las ausencias cuentan como ocupado.</param>
public record DashboardDto(
    int Days,
    DateOnly From,
    DateOnly To,
    DashboardTodayDto Today,
    DashboardKpis Kpis,
    IReadOnlyList<DailyBookingsDto> Daily,
    IReadOnlyList<HeatCellDto> Heatmap,
    IReadOnlyList<CourtStatsDto> Courts,
    IReadOnlyList<TopCustomerDto> TopCustomers);

public interface IDashboardService
{
    /// <summary>Métricas de los últimos <paramref name="days"/> días (hasta hoy inclusive) contra el período anterior.</summary>
    Task<DashboardDto> GetAsync(Guid clubId, int days, CancellationToken ct = default);
}

/// <summary>
/// Métricas para la pantalla de inicio del panel. Todo se calcula sobre la grilla del club:
/// ocupación = turnos de la grilla con una reserva no cancelada encima / turnos de la grilla.
/// </summary>
public class DashboardService(EZmatchDbContext db, TimeProvider time) : IDashboardService
{
    public static readonly int[] AllowedDays = [7, 30, 90];
    private const int TopCustomersCount = 5;

    private sealed record Slot(Guid CourtId, DateOnly Date, int Hour, DateTime StartsAt, DateTime EndsAt, bool Booked);

    /// <inheritdoc />
    public async Task<DashboardDto> GetAsync(Guid clubId, int days, CancellationToken ct = default)
    {
        if (!AllowedDays.Contains(days))
        {
            throw new AppException("El período tiene que ser de 7, 30 o 90 días.", StatusCodes.Status400BadRequest, "invalid_period");
        }

        var club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clubId, ct)
            ?? throw AppException.NotFound("El club no existe.");
        var zone = ClubTime.Zone(club.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(ClubTime.ToLocal(now, zone));

        var to = today;
        var from = today.AddDays(1 - days);
        var prevFrom = from.AddDays(-days);
        var rangeStart = ClubTime.ToUtc(prevFrom, TimeOnly.MinValue, zone);
        var rangeEnd = ClubTime.ToUtc(to.AddDays(2), TimeOnly.MinValue, zone);   // +1 día por turnos que cruzan la medianoche

        var courts = await db.Courts.AsNoTracking()
            .Include(c => c.SlotTemplates)
            .Where(c => c.ClubId == clubId && c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);
        var courtIds = courts.Select(c => c.Id).ToList();

        var bookings = await db.Bookings.AsNoTracking()
            .Include(b => b.Customer).Include(b => b.Court)
            .Where(b => b.ClubId == clubId && b.StartsAt >= rangeStart && b.StartsAt < rangeEnd)
            .ToListAsync(ct);
        var active = bookings.Where(b => b.Status != BookingStatus.Cancelled).ToList();
        DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ClubTime.ToLocal(utc, zone));

        // Turnos de la grilla de cada día, marcados como reservados si una reserva activa los pisa.
        List<Slot> SlotsOf(DateOnly first, DateOnly last)
        {
            var result = new List<Slot>();
            for (var d = first; d <= last; d = d.AddDays(1))
            {
                foreach (var court in courts)
                {
                    foreach (var s in SlotCalculator.ForDate(court, d, zone))
                    {
                        var booked = active.Any(b => b.CourtId == s.CourtId && s.Overlaps(b.StartsAt, b.EndsAt));
                        result.Add(new Slot(s.CourtId, d, s.StartTime.Hour, s.StartsAt, s.EndsAt, booked));
                    }
                }
            }
            return result;
        }

        var slots = SlotsOf(from, to);
        var prevSlots = SlotsOf(prevFrom, from.AddDays(-1));
        static decimal Ratio(int part, int whole) => whole == 0 ? 0 : Math.Round((decimal)part / whole, 4);

        // Reservas por período (por el día local en que empiezan).
        var current = active.Where(b => LocalDate(b.StartsAt) >= from && LocalDate(b.StartsAt) <= to).ToList();
        var previous = active.Where(b => LocalDate(b.StartsAt) >= prevFrom && LocalDate(b.StartsAt) < from).ToList();

        Kpi Pair(Func<List<Booking>, decimal> f) => new(f(current), f(previous));
        var newCustomersNow = await CountNewCustomersAsync(clubId, from, to.AddDays(1), zone, ct);
        var newCustomersPrev = await CountNewCustomersAsync(clubId, prevFrom, from, zone, ct);

        var kpis = new DashboardKpis(
            Occupancy: new Kpi(Ratio(slots.Count(s => s.Booked), slots.Count), Ratio(prevSlots.Count(s => s.Booked), prevSlots.Count)),
            Bookings: Pair(l => l.Count),
            PaidRevenue: Pair(l => l.Where(b => b.PaymentStatus == PaymentStatus.Paid).Sum(b => b.Price)),
            WhatsAppShare: Pair(l => Ratio(l.Count(b => b.Source == BookingSource.WhatsApp), l.Count)),
            NoShowRate: Pair(l => Ratio(l.Count(b => b.Status == BookingStatus.NoShow), l.Count(b => b.StartsAt <= now))),
            NewCustomers: new Kpi(newCustomersNow, newCustomersPrev));

        var daily = new List<DailyBookingsDto>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var day = current.Where(b => LocalDate(b.StartsAt) == d).ToList();
            daily.Add(new DailyBookingsDto(d, day.Count(b => b.Source == BookingSource.WhatsApp), day.Count(b => b.Source == BookingSource.Panel)));
        }

        var heatmap = slots
            .GroupBy(s => (s.Date.DayOfWeek, s.Hour))
            .Select(g => new HeatCellDto(g.Key.DayOfWeek, g.Key.Hour, g.Count(), g.Count(s => s.Booked)))
            .OrderBy(c => ((int)c.DayOfWeek + 6) % 7).ThenBy(c => c.Hour)
            .ToList();

        var courtStats = courts.Select(c =>
        {
            var mine = slots.Where(s => s.CourtId == c.Id).ToList();
            var booked = mine.Count(s => s.Booked);
            return new CourtStatsDto(c.Id, c.Name, mine.Count, booked, Ratio(booked, mine.Count));
        }).ToList();

        var top = current
            .GroupBy(b => b.CustomerId)
            .Select(g => new TopCustomerDto(g.Key, g.First().Customer.Name, g.First().Customer.Phone,
                g.Count(), g.Count(b => b.Status == BookingStatus.NoShow)))
            .OrderByDescending(t => t.Bookings).ThenBy(t => t.Name)
            .Take(TopCustomersCount)
            .ToList();

        // Hoy.
        var todaySlots = slots.Where(s => s.Date == today).ToList();
        var todayBookings = active.Where(b => LocalDate(b.StartsAt) == today).ToList();
        var next = todayBookings
            .Where(b => b.Status == BookingStatus.Confirmed && b.StartsAt > now)
            .OrderBy(b => b.StartsAt)
            .Select(b => new NextBookingDto(TimeOnly.FromDateTime(ClubTime.ToLocal(b.StartsAt, zone)), b.Court.Name, b.Customer.Name))
            .FirstOrDefault();
        var todayDto = new DashboardTodayDto(
            todayBookings.Count,
            Ratio(todaySlots.Count(s => s.Booked), todaySlots.Count),
            todayBookings.Where(b => b.PaymentStatus == PaymentStatus.Paid).Sum(b => b.Price),
            todayBookings.Where(b => b.PaymentStatus == PaymentStatus.Unpaid && b.Status != BookingStatus.NoShow).Sum(b => b.Price),
            next);

        return new DashboardDto(days, from, to, todayDto, kpis, daily, heatmap, courtStats, top);
    }

    private Task<int> CountNewCustomersAsync(Guid clubId, DateOnly from, DateOnly toExclusive, TimeZoneInfo zone, CancellationToken ct)
    {
        var start = ClubTime.ToUtc(from, TimeOnly.MinValue, zone);
        var end = ClubTime.ToUtc(toExclusive, TimeOnly.MinValue, zone);
        return db.Customers.CountAsync(c => c.ClubId == clubId && c.CreatedAt >= start && c.CreatedAt < end, ct);
    }
}
