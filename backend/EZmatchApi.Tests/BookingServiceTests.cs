using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using EZmatchApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EZmatchApi.Tests;

[Collection(ApiCollection.Name)]
public class BookingServiceTests(ApiFixture fixture)
{
    // ApiFixture.Now = lunes 2026-10-05 09:00 hora Argentina.
    private static readonly DateOnly Today = new(2026, 10, 5);
    private static readonly DateOnly Tomorrow = Today.AddDays(1);
    private static readonly TimeOnly EightPm = new(20, 0);
    private const string Phone = "+5493415550001";

    private Task<BookingDto> CreateAsync(Guid clubId, CreateBookingRequest request, BookingSource source = BookingSource.WhatsApp) =>
        fixture.RunAsync(sp => sp.GetRequiredService<IBookingService>().CreateAsync(clubId, request, source));

    private static CreateBookingRequest Request(DateOnly date, TimeOnly start, string phone = Phone, string? name = "Juan") =>
        new(Sport.Padel, date, start, phone, name);

    [Fact]
    public async Task Create_AssignsCourtsInOrder_ThenConflictsWithAlternatives()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 3);

        var courts = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            courts.Add((await CreateAsync(club.Id, Request(Tomorrow, EightPm, $"+54934155500{i:00}"))).CourtName);
        }
        Assert.Equal(["Cancha 1", "Cancha 2", "Cancha 3"], courts);

        var ex = await Assert.ThrowsAsync<AppException>(() => CreateAsync(club.Id, Request(Tomorrow, EightPm, "+5493415550099")));
        Assert.Equal("conflict", ex.Code);

        var alternatives = (IReadOnlyList<AvailableSlotDto>)ex.Details!.GetType().GetProperty("alternatives")!.GetValue(ex.Details)!;
        Assert.Equal([new TimeOnly(17, 0), new TimeOnly(18, 30), new TimeOnly(21, 30)], alternatives.Select(a => a.StartTime));
    }

    [Fact]
    public async Task Create_ConcurrentRequests_NeverDoubleBook()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 3);

        var attempts = Enumerable.Range(0, 12).Select(i => Task.Run(async () =>
        {
            try
            {
                await CreateAsync(club.Id, Request(Tomorrow, EightPm, $"+54934100000{i:00}", $"Jugador {i}"), BookingSource.Panel);
                return true;
            }
            catch (AppException ex) when (ex.Code == "conflict")
            {
                return false;
            }
        }));
        var results = await Task.WhenAll(attempts);

        Assert.Equal(3, results.Count(ok => ok));
        var perCourt = await fixture.RunAsync(sp => sp.GetRequiredService<EZmatchDbContext>().Bookings
            .Where(b => b.ClubId == club.Id).GroupBy(b => b.CourtId).Select(g => g.Count()).ToListAsync());
        Assert.Equal([1, 1, 1], perCourt);
    }

    [Fact]
    public async Task Database_RejectsOverlappingBookings_ButIgnoresCancelled()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var courtId = club.Courts[0].Id;
        var start = new DateTime(2026, 10, 6, 23, 0, 0, DateTimeKind.Utc);

        await fixture.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            var customer = new Customer { ClubId = club.Id, Phone = Phone, Name = "Juan" };
            db.Customers.Add(customer);
            db.Bookings.Add(new Booking
            {
                ClubId = club.Id, CourtId = courtId, CustomerId = customer.Id, Source = BookingSource.Panel,
                StartsAt = start, EndsAt = start.AddMinutes(90), Status = BookingStatus.Cancelled,
            });
            db.Bookings.Add(new Booking
            {
                ClubId = club.Id, CourtId = courtId, CustomerId = customer.Id, Source = BookingSource.Panel,
                StartsAt = start, EndsAt = start.AddMinutes(90),
            });
            await db.SaveChangesAsync(); // la cancelada no cuenta

            // Se saltea el servicio a propósito: la base sola tiene que impedir el solapamiento.
            db.Bookings.Add(new Booking
            {
                ClubId = club.Id, CourtId = courtId, CustomerId = customer.Id, Source = BookingSource.Panel,
                StartsAt = start.AddMinutes(60), EndsAt = start.AddMinutes(120),
            });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ExclusionViolation, ((PostgresException)ex.InnerException!).SqlState);
        });
    }

    [Fact]
    public async Task Create_StoresUtc_FromClubLocalTime()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);

        var booking = await CreateAsync(club.Id, Request(Tomorrow, new TimeOnly(8, 0)));

        // Argentina es UTC-3 todo el año.
        Assert.Equal(new DateTime(2026, 10, 6, 11, 0, 0, DateTimeKind.Utc), booking.StartsAt);
        Assert.Equal(new DateTime(2026, 10, 6, 12, 30, 0, DateTimeKind.Utc), booking.EndsAt);
        Assert.Equal(Tomorrow, booking.Date);
        Assert.Equal(new TimeOnly(9, 30), booking.EndTime);
        Assert.Equal(24000m, booking.Price);
    }

    [Fact]
    public async Task Create_SlotCrossingMidnight_OccupiesNextDaySlot()
    {
        // Lunes 23:30 (90 min, termina martes 01:00) y martes 00:30 en la misma cancha.
        var club = await fixture.CreateClubAsync(padelCourts: 1, grid: () =>
        [
            new SlotTemplate { DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(23, 30), DurationMinutes = 90, Price = 30000m },
            new SlotTemplate { DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(0, 30), DurationMinutes = 60, Price = 30000m },
        ]);

        var booking = await CreateAsync(club.Id, Request(Today, new TimeOnly(23, 30)));
        Assert.Equal(Today, booking.Date);
        Assert.Equal(new TimeOnly(1, 0), booking.EndTime);
        Assert.Equal(new DateTime(2026, 10, 6, 4, 0, 0, DateTimeKind.Utc), booking.EndsAt);

        var tuesday = await fixture.RunAsync(sp => sp.GetRequiredService<IAvailabilityService>()
            .GetAvailableAsync(club.Id, new AvailabilityQuery(Tomorrow), enforceBookingWindow: true));
        Assert.Empty(tuesday);

        var ex = await Assert.ThrowsAsync<AppException>(() => CreateAsync(club.Id, Request(Tomorrow, new TimeOnly(0, 30), "+5493415550002")));
        Assert.Equal("conflict", ex.Code);
    }

    [Fact]
    public async Task Create_FromBot_EnforcesBookingWindow_PanelDoesNot()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);

        var tooSoon = await Assert.ThrowsAsync<AppException>(() => CreateAsync(club.Id, Request(Today, new TimeOnly(8, 0))));
        Assert.Equal("too_soon", tooSoon.Code);

        var tooFar = await Assert.ThrowsAsync<AppException>(() => CreateAsync(club.Id, Request(Today.AddDays(20), EightPm)));
        Assert.Equal("too_far", tooFar.Code);

        // 09:30 con "ahora" 09:00 y anticipación mínima de 30 min: justo en el límite, se acepta.
        await CreateAsync(club.Id, Request(Today, new TimeOnly(9, 30)));

        // El panel puede registrar un turno que ya empezó (ej. alguien que llegó sin reservar).
        var walkIn = await CreateAsync(club.Id, Request(Today, new TimeOnly(8, 0), "+5493415550002"), BookingSource.Panel);
        Assert.Equal(BookingSource.Panel, walkIn.Source);
    }

    [Fact]
    public async Task Create_NewCustomerWithoutName_IsRejected_ExistingCustomerDoesNotNeedIt()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 2);

        var ex = await Assert.ThrowsAsync<AppException>(() => CreateAsync(club.Id, Request(Tomorrow, EightPm, name: null)));
        Assert.Equal("customer_name_required", ex.Code);
        Assert.Equal(422, ex.StatusCode);

        await CreateAsync(club.Id, Request(Tomorrow, EightPm, "+54 9 341 555-0001", name: "Juan"));
        var second = await CreateAsync(club.Id, Request(Tomorrow, new TimeOnly(21, 30), name: null));
        Assert.Equal("Juan", second.CustomerName);
        Assert.Equal(Phone, second.CustomerPhone);
    }

    [Fact]
    public async Task Create_FromBot_EnforcesMaxActiveBookings_AndBlockedCustomers()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1, c => c.MaxActiveBookingsPerCustomer = 2);

        await CreateAsync(club.Id, Request(Tomorrow, new TimeOnly(17, 0)));
        await CreateAsync(club.Id, Request(Tomorrow, new TimeOnly(18, 30)));
        var max = await Assert.ThrowsAsync<AppException>(() => CreateAsync(club.Id, Request(Tomorrow, EightPm)));
        Assert.Equal("max_active_bookings", max.Code);

        // Desde el panel no hay límite.
        await CreateAsync(club.Id, Request(Tomorrow, EightPm), BookingSource.Panel);

        await fixture.RunAsync(sp => sp.GetRequiredService<EZmatchDbContext>().Customers
            .Where(c => c.ClubId == club.Id && c.Phone == Phone)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsBlocked, true)));
        var blocked = await Assert.ThrowsAsync<AppException>(() => CreateAsync(club.Id, Request(Tomorrow, new TimeOnly(21, 30))));
        Assert.Equal("customer_blocked", blocked.Code);
    }

    [Fact]
    public async Task Create_TimeNotInGrid_IsRejected()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);

        var ex = await Assert.ThrowsAsync<AppException>(() => CreateAsync(club.Id, Request(Tomorrow, new TimeOnly(20, 15))));
        Assert.Equal("slot_not_in_grid", ex.Code);
    }

    [Fact]
    public async Task Cancel_FromBot_OnlyOwnBookings_WithMinimumNotice()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1, c => c.CancellationMinHours = 3);
        var soon = await CreateAsync(club.Id, Request(Today, new TimeOnly(11, 0)));   // en 2 horas
        var later = await CreateAsync(club.Id, Request(Tomorrow, EightPm));

        Task<BookingDto> Cancel(Guid id, string? phone, BookingSource source) =>
            fixture.RunAsync(sp => sp.GetRequiredService<IBookingService>().CancelAsync(club.Id, id, phone, source));

        var foreign = await Assert.ThrowsAsync<AppException>(() => Cancel(later.Id, "+5493419999999", BookingSource.WhatsApp));
        Assert.Equal("not_found", foreign.Code);

        var tooLate = await Assert.ThrowsAsync<AppException>(() => Cancel(soon.Id, Phone, BookingSource.WhatsApp));
        Assert.Equal("cancellation_too_late", tooLate.Code);

        // El panel puede cancelar siempre.
        Assert.Equal(BookingStatus.Cancelled, (await Cancel(soon.Id, null, BookingSource.Panel)).Status);

        Assert.Equal(BookingStatus.Cancelled, (await Cancel(later.Id, Phone, BookingSource.WhatsApp)).Status);
        var again = await Assert.ThrowsAsync<AppException>(() => Cancel(later.Id, Phone, BookingSource.WhatsApp));
        Assert.Equal("booking_not_active", again.Code);

        // El turno cancelado queda libre otra vez.
        var rebooked = await CreateAsync(club.Id, Request(Tomorrow, EightPm, "+5493415550002"));
        Assert.Equal(later.CourtId, rebooked.CourtId);
    }

    [Fact]
    public async Task GetUpcomingForPhone_ReturnsOnlyActiveFutureBookingsOfThatPhone()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 2);
        var mine = await CreateAsync(club.Id, Request(Tomorrow, EightPm));
        var cancelled = await CreateAsync(club.Id, Request(Tomorrow, new TimeOnly(21, 30)));
        await fixture.RunAsync(sp => sp.GetRequiredService<IBookingService>()
            .CancelAsync(club.Id, cancelled.Id, Phone, BookingSource.WhatsApp));
        await CreateAsync(club.Id, Request(Tomorrow, EightPm, "+5493415550002"));

        var upcoming = await fixture.RunAsync(sp => sp.GetRequiredService<IBookingService>()
            .GetUpcomingForPhoneAsync(club.Id, "+54 9 341 555 0001"));

        Assert.Equal([mine.Id], upcoming.Select(b => b.Id));
    }
}
