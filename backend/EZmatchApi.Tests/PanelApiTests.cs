using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EZmatchApi.Tests;

/// <summary>Agenda y operación del mostrador por HTTP, como las usa el panel.</summary>
[Collection(ApiCollection.Name)]
public class PanelApiTests(ApiFixture fixture)
{
    // ApiFixture.Now = lunes 2026-10-05 09:00 hora Argentina.
    private const string Tomorrow = "2026-10-06";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(Club Club, HttpClient Owner)> ClubWithOwnerAsync(int courts = 2, Func<List<SlotTemplate>>? grid = null)
    {
        var club = await fixture.CreateClubAsync(courts, grid: grid);
        var owner = await fixture.CreateUserAsync(UserRole.Owner, club.Id);
        return (club, fixture.ClientFor(owner));
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid courtId, string date, string time,
        string phone = "341 555 0001", string? name = "Juan Pérez") =>
        client.PostAsJsonAsync("/api/bookings", new { courtId, date, startTime = time, phone, customerName = name });

    private static JsonElement CourtItems(JsonElement agenda, Guid courtId) =>
        agenda.GetProperty("courts").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == courtId).GetProperty("items");

    [Fact]
    public async Task Agenda_ShowsBookingsBlocksAndFreeSlots_WithDaySummary()
    {
        var (club, owner) = await ClubWithOwnerAsync();
        var court1 = club.Courts[0].Id;
        var court2 = club.Courts[1].Id;

        var paid = await JsonAsync(await BookAsync(owner, court1, Tomorrow, "20:00"));
        await owner.PutAsJsonAsync($"/api/bookings/{paid.GetProperty("id").GetGuid()}/payment", new { paid = true });
        Assert.Equal(HttpStatusCode.Created, (await BookAsync(owner, court1, Tomorrow, "21:30", "+5493415550002", "Lucía")).StatusCode);
        await fixture.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            db.Blocks.Add(new Block
            {
                CourtId = court2, Reason = "Torneo",
                StartsAt = new DateTime(2026, 10, 6, 11, 0, 0, DateTimeKind.Utc),   // 08:00 local
                EndsAt = new DateTime(2026, 10, 6, 14, 0, 0, DateTimeKind.Utc),     // 11:00 local
            });
            await db.SaveChangesAsync();
        });

        var agenda = await JsonAsync(await owner.GetAsync($"/api/agenda?date={Tomorrow}"));

        Assert.Equal("2026-10-05", agenda.GetProperty("today").GetString());
        var c1 = CourtItems(agenda, court1);
        var booking = c1.EnumerateArray().Single(i => i.GetProperty("startTime").GetString() == "20:00:00");
        Assert.Equal("Booking", booking.GetProperty("kind").GetString());
        Assert.Equal(20 * 60, booking.GetProperty("startMinute").GetInt32());
        Assert.Equal(90, booking.GetProperty("durationMinutes").GetInt32());
        Assert.Equal("+5493415550001", booking.GetProperty("booking").GetProperty("customerPhone").GetString());
        Assert.Equal("Panel", booking.GetProperty("booking").GetProperty("source").GetString());
        Assert.Equal(11, c1.GetArrayLength());   // 9 libres + 2 reservas

        var c2 = CourtItems(agenda, court2).EnumerateArray().ToList();
        Assert.Equal("Block", c2[0].GetProperty("kind").GetString());
        Assert.Equal("Torneo", c2[0].GetProperty("blockReason").GetString());
        Assert.DoesNotContain(c2, i => i.GetProperty("kind").GetString() == "Free" && i.GetProperty("startMinute").GetInt32() < 11 * 60);

        var summary = agenda.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("bookings").GetInt32());
        Assert.Equal(30000m, summary.GetProperty("paidAmount").GetDecimal());
        Assert.Equal(30000m, summary.GetProperty("pendingAmount").GetDecimal());
        // Cancha 2: 11 turnos menos 2 tapados por el torneo (08:00 y 09:30; el de 11:00 arranca cuando termina).
        Assert.Equal(9 + 9, summary.GetProperty("freeSlots").GetInt32());
    }

    [Fact]
    public async Task Agenda_BookingCrossingMidnight_DrawsOnItsDay_AndMarksNextDaySlotBusy()
    {
        var (club, owner) = await ClubWithOwnerAsync(courts: 1, grid: () =>
        [
            new SlotTemplate { DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(23, 30), DurationMinutes = 90, Price = 30000m },
            new SlotTemplate { DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(0, 30), DurationMinutes = 60, Price = 30000m },
        ]);
        var court = club.Courts[0].Id;
        await BookAsync(owner, court, "2026-10-05", "23:30");

        var monday = CourtItems(await JsonAsync(await owner.GetAsync("/api/agenda?date=2026-10-05")), court).EnumerateArray().Single();
        Assert.Equal(23 * 60 + 30, monday.GetProperty("startMinute").GetInt32());
        Assert.Equal("01:00:00", monday.GetProperty("endTime").GetString());

        var tuesday = CourtItems(await JsonAsync(await owner.GetAsync($"/api/agenda?date={Tomorrow}")), court).EnumerateArray().Single();
        Assert.Equal("Busy", tuesday.GetProperty("kind").GetString());
        Assert.Equal(30, tuesday.GetProperty("startMinute").GetInt32());
    }

    [Fact]
    public async Task ClubsAreIsolated_AndSuperAdminChoosesClubByHeader()
    {
        var (clubA, ownerA) = await ClubWithOwnerAsync(courts: 1);
        var (clubB, ownerB) = await ClubWithOwnerAsync(courts: 1);
        var bookingA = (await JsonAsync(await BookAsync(ownerA, clubA.Courts[0].Id, Tomorrow, "20:00"))).GetProperty("id").GetGuid();

        // El dueño de B no puede operar reservas ni canchas de A (404: no se revela que existen).
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.PutAsJsonAsync($"/api/bookings/{bookingA}/payment", new { paid = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.PostAsJsonAsync($"/api/bookings/{bookingA}/cancel", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await BookAsync(ownerB, clubA.Courts[0].Id, Tomorrow, "21:30")).StatusCode);

        // Aunque mande el header del SuperAdmin, sigue viendo solo su club.
        ownerB.DefaultRequestHeaders.Add("X-Club-Id", clubA.Id.ToString());
        var agendaB = await JsonAsync(await ownerB.GetAsync($"/api/agenda?date={Tomorrow}"));
        Assert.Equal(clubB.Courts[0].Id, agendaB.GetProperty("courts")[0].GetProperty("id").GetGuid());

        var admin = await fixture.CreateUserAsync(UserRole.SuperAdmin);
        var agendaA = await JsonAsync(await fixture.ClientFor(admin, clubA.Id).GetAsync($"/api/agenda?date={Tomorrow}"));
        Assert.Equal(clubA.Courts[0].Id, agendaA.GetProperty("courts")[0].GetProperty("id").GetGuid());

        var noClub = await fixture.ClientFor(admin).GetAsync("/api/agenda");
        Assert.Equal("club_required", (await JsonAsync(noClub)).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.CreateClient().GetAsync("/api/agenda")).StatusCode);
    }

    [Fact]
    public async Task NoShow_OnlyAfterStart_AndExcludedFromPending()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var staff = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Staff, club.Id));
        var court = club.Courts[0].Id;

        // Walk-in de hoy 08:00 (ya empezó: "ahora" son las 09:00).
        var started = (await JsonAsync(await BookAsync(staff, court, "2026-10-05", "08:00"))).GetProperty("id").GetGuid();
        var future = (await JsonAsync(await BookAsync(staff, court, Tomorrow, "20:00"))).GetProperty("id").GetGuid();

        var tooEarly = await staff.PutAsJsonAsync($"/api/bookings/{future}/no-show", new { noShow = true });
        Assert.Equal("booking_not_started", (await JsonAsync(tooEarly)).GetProperty("code").GetString());

        var marked = await JsonAsync(await staff.PutAsJsonAsync($"/api/bookings/{started}/no-show", new { noShow = true }));
        Assert.Equal("NoShow", marked.GetProperty("status").GetString());

        var summary = (await JsonAsync(await staff.GetAsync("/api/agenda?date=2026-10-05"))).GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("noShows").GetInt32());
        Assert.Equal(0m, summary.GetProperty("pendingAmount").GetDecimal());

        var undone = await JsonAsync(await staff.PutAsJsonAsync($"/api/bookings/{started}/no-show", new { noShow = false }));
        Assert.Equal("Confirmed", undone.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PanelCancel_FreesTheSlot_AndCancelledBookingCannotBePaid()
    {
        var (club, owner) = await ClubWithOwnerAsync(courts: 1);
        var court = club.Courts[0].Id;
        // Hoy 11:00: faltan 2 h, por WhatsApp no se podría cancelar; desde el panel sí.
        var id = (await JsonAsync(await BookAsync(owner, court, "2026-10-05", "11:00"))).GetProperty("id").GetGuid();

        var cancelled = await JsonAsync(await owner.PostAsJsonAsync($"/api/bookings/{id}/cancel", new { reason = "Llamó para avisar" }));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());

        var slot = CourtItems(await JsonAsync(await owner.GetAsync("/api/agenda?date=2026-10-05")), court)
            .EnumerateArray().Single(i => i.GetProperty("startMinute").GetInt32() == 11 * 60);
        Assert.Equal("Free", slot.GetProperty("kind").GetString());

        var pay = await owner.PutAsJsonAsync($"/api/bookings/{id}/payment", new { paid = true });
        Assert.Equal("booking_not_active", (await JsonAsync(pay)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PanelBooking_ValidatesPhone_AndRecognizesExistingWhatsAppCustomer()
    {
        var (club, owner) = await ClubWithOwnerAsync(courts: 2);
        var court = club.Courts[0].Id;

        var invalid = await BookAsync(owner, court, Tomorrow, "20:00", phone: "555 0001");
        Assert.Equal("invalid_phone", (await JsonAsync(invalid)).GetProperty("code").GetString());

        // Cliente que ya reservó por WhatsApp: el mostrador lo tipea en formato local y es la misma persona.
        await fixture.BotClient().PostAsJsonAsync("/api/bot/bookings", new
        {
            inboxId = 0, phone = "+5493415550099", sport = "Padel", date = Tomorrow, startTime = "17:00", customerName = "Martín",
        });
        await fixture.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            db.Customers.Add(new Customer { ClubId = club.Id, Phone = "+5493415550099", Name = "Martín Díaz" });
            await db.SaveChangesAsync();
        });
        var walkIn = await JsonAsync(await BookAsync(owner, court, Tomorrow, "18:30", phone: "0341 555-0099", name: null));
        Assert.Equal("Martín Díaz", walkIn.GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task CustomerSearch_ByNameOrPhone_OnlyOwnClub()
    {
        var (club, owner) = await ClubWithOwnerAsync(courts: 1);
        var (otherClub, _) = await ClubWithOwnerAsync(courts: 1);
        await fixture.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            db.Customers.AddRange(
                new Customer { ClubId = club.Id, Phone = "+5493415551234", Name = "Ana Gómez" },
                new Customer { ClubId = club.Id, Phone = "+5491144449999", Name = "Pedro Ruiz" },
                new Customer { ClubId = otherClub.Id, Phone = "+5493415557777", Name = "Ana Otra" });
            await db.SaveChangesAsync();
        });

        var byName = await owner.GetFromJsonAsync<JsonElement>("/api/customers?search=ana");
        Assert.Equal(["Ana Gómez"], byName.EnumerateArray().Select(c => c.GetProperty("name").GetString()));

        var byPhone = await owner.GetFromJsonAsync<JsonElement>("/api/customers?search=4444");
        Assert.Equal(["Pedro Ruiz"], byPhone.EnumerateArray().Select(c => c.GetProperty("name").GetString()));

        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/customers?search=a")).GetArrayLength());
    }
}

public class ArgentinePhoneTests
{
    [Theory]
    [InlineData("341 555-0001", "+5493415550001")]
    [InlineData("0341 555 0001", "+5493415550001")]
    [InlineData("11 4444 9999", "+5491144449999")]
    [InlineData("54 341 5550001", "+5493415550001")]
    [InlineData("549 341 5550001", "+5493415550001")]
    [InlineData("+54 9 341 555-0001", "+5493415550001")]
    [InlineData("+598 99 123 456", "+59899123456")]
    [InlineData("005493415550001", "+5493415550001")]
    public void NormalizesToWhatsAppFormat(string raw, string expected) =>
        Assert.Equal(expected, PhoneNumber.NormalizeArgentine(raw));

    [Theory]
    [InlineData("555 0001")]
    [InlineData("0341 15 555 0001")]
    [InlineData("")]
    public void RejectsAmbiguousNumbers(string raw) =>
        Assert.Equal("invalid_phone", Assert.Throws<AppException>(() => PhoneNumber.NormalizeArgentine(raw)).Code);
}
