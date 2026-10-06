using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Models;
using EZmatchApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EZmatchApi.Tests;

/// <summary>Turnos fijos, agenda semanal y avisos de actividad del bot.</summary>
[Collection(ApiCollection.Name)]
public class FixedBookingApiTests(ApiFixture fixture)
{
    // ApiFixture.Now = lunes 2026-10-05 09:00 hora Argentina. Horizonte de turnos fijos: 35 días (hasta el 9/11).
    private static readonly TimeZoneInfo Zone = ClubTime.Zone("America/Argentina/Buenos_Aires");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private Task<List<Booking>> FixedBookingsAsync(Guid fixedId) => fixture.RunAsync(sp =>
        sp.GetRequiredService<EZmatchDbContext>().Bookings.AsNoTracking()
            .Where(b => b.FixedBookingId == fixedId).OrderBy(b => b.StartsAt).ToListAsync());

    [Fact]
    public async Task FixedBooking_BooksWeeks_SkipsBusyDates_RespectsCancellations_AndEnds()
    {
        var inboxId = ApiFixture.NextInboxId();
        var club = await fixture.CreateClubAsync(padelCourts: 1, c => c.ChatwootInboxId = inboxId);
        var court = club.Courts[0];
        // El martes 20/10 a las 20:00 ya está reservado por otra persona.
        var busy = await fixture.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            var other = new Customer { ClubId = club.Id, Name = "Otro", Phone = "+5493415559999" };
            var start = ClubTime.ToUtc(new DateOnly(2026, 10, 20), new TimeOnly(20, 0), Zone);
            var booking = new Booking
            {
                ClubId = club.Id, CourtId = court.Id, Customer = other, StartsAt = start, EndsAt = start.AddMinutes(90),
                Price = 30000m, Source = BookingSource.Panel,
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            return booking.Id;
        });
        var staff = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Staff, club.Id));
        var body = new { courtId = court.Id, dayOfWeek = "Tuesday", startTime = "20:00", phone = "341 555-0001", customerName = "Juan" };

        var created = await staff.PostAsJsonAsync("/api/fixed-bookings", body);
        var dto = await JsonAsync(created);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("2026-10-06", dto.GetProperty("nextDate").GetString());
        Assert.Equal("21:30:00", dto.GetProperty("endTime").GetString());
        Assert.Equal(["2026-10-20"], dto.GetProperty("missingDates").EnumerateArray().Select(d => d.GetString()));
        var fixedId = dto.GetProperty("id").GetGuid();
        var generated = await FixedBookingsAsync(fixedId);
        Assert.Equal(4, generated.Count);   // 6, 13 y 27/10 y 3/11
        Assert.All(generated, b => Assert.Equal(BookingStatus.Confirmed, b.Status));

        var duplicate = await staff.PostAsJsonAsync("/api/fixed-bookings", body);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("fixed_booking_exists", (await JsonAsync(duplicate)).GetProperty("code").GetString());

        // La agenda marca la reserva como turno fijo, y la semana también.
        var agenda = await staff.GetFromJsonAsync<JsonElement>("/api/agenda?date=2026-10-06");
        var item = agenda.GetProperty("courts")[0].GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("kind").GetString() == "Booking");
        Assert.Equal(fixedId, item.GetProperty("booking").GetProperty("fixedBookingId").GetGuid());
        var week = await staff.GetFromJsonAsync<JsonElement>("/api/agenda/week?date=2026-10-08");
        Assert.Equal("2026-10-05", week.GetProperty("start").GetString());
        var tuesday = week.GetProperty("days")[1];
        Assert.Equal("2026-10-06", tuesday.GetProperty("date").GetString());
        Assert.True(tuesday.GetProperty("bookings")[0].GetProperty("isFixed").GetBoolean());
        Assert.Equal("Juan", tuesday.GetProperty("bookings")[0].GetProperty("customerName").GetString());

        // Los turnos fijos no cuentan para el máximo de reservas por WhatsApp (2).
        var botBooking = await fixture.BotClient().PostAsJsonAsync("/api/bot/bookings", new
        {
            inboxId, phone = "+5493415550001", sport = "padel", date = "2026-10-07", startTime = "20:00",
        });
        Assert.Equal(HttpStatusCode.Created, botBooking.StatusCode);

        // Cancelar un martes suelto no lo vuelve a crear; liberar el 20/10 sí lo completa.
        var oct13 = generated[1];
        (await staff.PostAsJsonAsync($"/api/bookings/{oct13.Id}/cancel", new { reason = "Viaja" })).EnsureSuccessStatusCode();
        (await staff.PostAsJsonAsync($"/api/bookings/{busy}/cancel", new { reason = (string?)null })).EnsureSuccessStatusCode();
        var createdNow = await fixture.RunAsync(sp => sp.GetRequiredService<IFixedBookingService>().MaterializeAsync(club.Id));
        Assert.Equal(1, createdNow);
        var after = await FixedBookingsAsync(fixedId);
        Assert.Equal(5, after.Count);
        Assert.Equal(BookingStatus.Cancelled, after.Single(b => b.Id == oct13.Id).Status);

        // Baja: cancela todas las futuras.
        var ended = await JsonAsync(await staff.PostAsync($"/api/fixed-bookings/{fixedId}/end", null));
        Assert.False(ended.GetProperty("isActive").GetBoolean());
        Assert.All(await FixedBookingsAsync(fixedId), b => Assert.Equal(BookingStatus.Cancelled, b.Status));
        var list = await staff.GetFromJsonAsync<JsonElement>("/api/fixed-bookings");
        Assert.False(list[0].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task FixedBooking_SlotNotInGrid_Returns400()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var staff = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Staff, club.Id));

        var response = await staff.PostAsJsonAsync("/api/fixed-bookings", new
        {
            courtId = club.Courts[0].Id, dayOfWeek = "Tuesday", startTime = "20:15", phone = "3415550001", customerName = "Juan",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("slot_not_in_grid", (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Activity_ListsBotBookingsAndCancellations_NotPanelOnes()
    {
        var inboxId = ApiFixture.NextInboxId();
        var club = await fixture.CreateClubAsync(padelCourts: 1, c => c.ChatwootInboxId = inboxId);
        var staff = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Staff, club.Id));
        var bot = fixture.BotClient();

        var booked = await JsonAsync(await bot.PostAsJsonAsync("/api/bot/bookings", new
        {
            inboxId, phone = "+5493415550001", sport = "padel", date = "2026-10-07", startTime = "20:00", customerName = "Ana",
        }));
        var bookingId = booked.GetProperty("booking").GetProperty("id").GetGuid();
        (await bot.PostAsJsonAsync($"/api/bot/bookings/{bookingId}/cancel", new { inboxId, phone = "+5493415550001" }))
            .EnsureSuccessStatusCode();
        (await staff.PostAsJsonAsync("/api/bookings", new
        {
            courtId = club.Courts[0].Id, date = "2026-10-08", startTime = "20:00", phone = "3415550002", customerName = "Mostrador",
        })).EnsureSuccessStatusCode();

        var activity = (await staff.GetFromJsonAsync<JsonElement>("/api/activity")).EnumerateArray().ToList();

        Assert.Equal(2, activity.Count);
        Assert.All(activity, a => Assert.Equal("Ana", a.GetProperty("customerName").GetString()));
        Assert.Equal(["Cancelled", "Booked"], activity.Select(a => a.GetProperty("kind").GetString()));
        Assert.Equal("2026-10-07", activity[0].GetProperty("date").GetString());
    }
}
