using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EZmatchApi.Models;

namespace EZmatchApi.Tests;

/// <summary>Configuración del club por HTTP: datos, canchas, grilla y bloqueos.</summary>
[Collection(ApiCollection.Name)]
public class ConfigApiTests(ApiFixture fixture)
{
    // ApiFixture.Now = lunes 2026-10-05 09:00 hora Argentina.
    private const string Tomorrow = "2026-10-06";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(Club Club, HttpClient Owner, HttpClient Staff)> ClubAsync(int courts = 2, Action<Club>? configure = null)
    {
        var club = await fixture.CreateClubAsync(courts, configure);
        var owner = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Owner, club.Id));
        var staff = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Staff, club.Id));
        return (club, owner, staff);
    }

    private static object Settings(string? assistant = "Mati", int horizon = 14) => new
    {
        name = "Pádel Norte", address = "Calle 1", phone = "+5493410000000", assistantName = assistant,
        botInstructions = "Se alquilan paletas.", cancellationMinHours = 6, minLeadMinutes = 60,
        bookingHorizonDays = horizon, maxActiveBookingsPerCustomer = 3,
    };

    [Fact]
    public async Task Settings_OwnerUpdates_StaffCannot_AndBotUsesThem()
    {
        var inboxId = ApiFixture.NextInboxId();
        var (_, owner, staff) = await ClubAsync(1, c => c.ChatwootInboxId = inboxId);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/club", Settings())).StatusCode);
        var invalid = await owner.PutAsJsonAsync("/api/club", Settings(horizon: 0));
        Assert.Equal("validation_error", (await JsonAsync(invalid)).GetProperty("code").GetString());

        var updated = await JsonAsync(await owner.PutAsJsonAsync("/api/club", Settings()));
        Assert.Equal("Mati", updated.GetProperty("assistantName").GetString());
        Assert.Equal(6, (await staff.GetFromJsonAsync<JsonElement>("/api/club")).GetProperty("cancellationMinHours").GetInt32());

        var context = await fixture.BotClient().GetFromJsonAsync<JsonElement>($"/api/bot/context?inboxId={inboxId}");
        Assert.Equal("Mati", context.GetProperty("assistantName").GetString());
        var summary = context.GetProperty("summary").GetString();
        Assert.Contains("Club: Pádel Norte", summary);
        Assert.Contains("Nombre del asistente: Mati", summary);
        Assert.Contains("con al menos 6 horas de anticipación", summary);
        Assert.Contains("Se alquilan paletas.", summary);
    }

    [Fact]
    public async Task Courts_CreateReorderAndDeactivate_ReflectedInAgenda()
    {
        var (club, owner, staff) = await ClubAsync(2);

        var created = await JsonAsync(await owner.PostAsJsonAsync("/api/courts", new { name = "Fútbol 5", sport = "Futbol5", isCovered = false, isActive = true }));
        Assert.Equal(3, created.GetProperty("sortOrder").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/courts", new { name = "X", sport = "Padel", isCovered = false, isActive = true })).StatusCode);

        var futbol = created.GetProperty("id").GetGuid();
        var order = new[] { futbol, club.Courts[1].Id, club.Courts[0].Id };
        await owner.PutAsJsonAsync("/api/courts/order", new { courtIds = order });
        var agenda = await owner.GetFromJsonAsync<JsonElement>($"/api/agenda?date={Tomorrow}");
        Assert.Equal(order, agenda.GetProperty("courts").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()));

        // Con una reserva por delante no se puede desactivar.
        await owner.PostAsJsonAsync("/api/bookings", new { courtId = club.Courts[0].Id, date = Tomorrow, startTime = "20:00", phone = "341 555 0001", customerName = "Juan" });
        var blocked = await owner.PutAsJsonAsync($"/api/courts/{club.Courts[0].Id}", new { name = "Cancha 1", sport = "Padel", isCovered = true, isActive = false });
        Assert.Equal("court_has_bookings", (await JsonAsync(blocked)).GetProperty("code").GetString());

        // Sin reservas, sí; y deja de aparecer en la agenda.
        var off = await owner.PutAsJsonAsync($"/api/courts/{club.Courts[1].Id}", new { name = "Cancha 2", sport = "Padel", isCovered = false, isActive = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        agenda = await owner.GetFromJsonAsync<JsonElement>($"/api/agenda?date={Tomorrow}");
        Assert.DoesNotContain(agenda.GetProperty("courts").EnumerateArray(), c => c.GetProperty("id").GetGuid() == club.Courts[1].Id);
    }

    [Fact]
    public async Task Generate_WithPeakPrice_KeepsOtherDays_AndHandlesPastMidnight()
    {
        var (club, owner, _) = await ClubAsync(1);
        var court = club.Courts[0].Id;

        // Lunes a viernes 08:00–23:00 cada 90, $25.000; desde las 17 $32.000.
        var weekdays = await JsonAsync(await owner.PostAsJsonAsync($"/api/courts/{court}/slots/generate", new
        {
            days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
            firstStart = "08:00", lastStart = "23:00", durationMinutes = 90, price = 25000, peakFrom = "17:00", peakPrice = 32000,
        }));
        var slots = weekdays.EnumerateArray().ToList();
        var monday = slots.Where(s => s.GetProperty("dayOfWeek").GetString() == "Monday").ToList();
        Assert.Equal(11, monday.Count);
        Assert.Equal(25000m, monday.Single(s => s.GetProperty("startTime").GetString() == "15:30:00").GetProperty("price").GetDecimal());
        Assert.Equal(32000m, monday.Single(s => s.GetProperty("startTime").GetString() == "17:00:00").GetProperty("price").GetDecimal());
        // El fin de semana conserva la grilla anterior (la default del test: 11 turnos por día).
        Assert.Equal(11, slots.Count(s => s.GetProperty("dayOfWeek").GetString() == "Saturday"));

        // Sábado de 18:00 a 01:00 cada hora: los de después de medianoche son del domingo y siguen en precio pico.
        var saturday = await JsonAsync(await owner.PostAsJsonAsync($"/api/courts/{court}/slots/generate", new
        {
            days = new[] { "Saturday" }, firstStart = "18:00", lastStart = "01:00", durationMinutes = 60, price = 40000, peakFrom = "20:00", peakPrice = 55000,
        }));
        var all = saturday.EnumerateArray().ToList();
        Assert.Equal(6, all.Count(s => s.GetProperty("dayOfWeek").GetString() == "Saturday"));
        var sundayEarly = all.Where(s => s.GetProperty("dayOfWeek").GetString() == "Sunday"
            && s.GetProperty("startTime").GetString()!.CompareTo("02:00:00") < 0).ToList();
        Assert.Equal(["00:00:00", "01:00:00"], sundayEarly.Select(s => s.GetProperty("startTime").GetString()));
        Assert.All(sundayEarly, s => Assert.Equal(55000m, s.GetProperty("price").GetDecimal()));
    }

    [Fact]
    public async Task ReplaceSlots_RejectsOverlaps_IncludingAcrossMidnightAndWeekWrap()
    {
        var (club, owner, _) = await ClubAsync(1);
        var url = $"/api/courts/{club.Courts[0].Id}/slots";

        async Task<string?> Code(params object[] slots) =>
            (await JsonAsync(await owner.PutAsJsonAsync(url, new { slots }))).TryGetProperty("code", out var c) ? c.GetString() : null;

        Assert.Equal("slot_overlap", await Code(
            new { dayOfWeek = "Monday", startTime = "20:00", durationMinutes = 90, price = 1 },
            new { dayOfWeek = "Monday", startTime = "21:00", durationMinutes = 60, price = 1 }));
        Assert.Equal("slot_overlap", await Code(
            new { dayOfWeek = "Monday", startTime = "23:30", durationMinutes = 90, price = 1 },
            new { dayOfWeek = "Tuesday", startTime = "00:30", durationMinutes = 60, price = 1 }));
        Assert.Equal("slot_overlap", await Code(
            new { dayOfWeek = "Sunday", startTime = "23:30", durationMinutes = 90, price = 1 },
            new { dayOfWeek = "Monday", startTime = "00:30", durationMinutes = 60, price = 1 }));

        var ok = await owner.PutAsJsonAsync(url, new
        {
            slots = new object[]
            {
                new { dayOfWeek = "Monday", startTime = "23:30", durationMinutes = 90, price = 1 },
                new { dayOfWeek = "Tuesday", startTime = "01:00", durationMinutes = 60, price = 1 },
            },
        });
        Assert.Equal(2, (await JsonAsync(ok)).GetArrayLength());
    }

    [Fact]
    public async Task CopySlots_ReplacesTargetGrids_OnlyWithinClub()
    {
        var (club, owner, _) = await ClubAsync(3);
        var (otherClub, _, _) = await ClubAsync(1);
        var source = club.Courts[0].Id;
        await owner.PutAsJsonAsync($"/api/courts/{source}/slots", new
        {
            slots = new object[] { new { dayOfWeek = "Monday", startTime = "19:00", durationMinutes = 60, price = 50000 } },
        });

        var bad = await owner.PostAsJsonAsync($"/api/courts/{source}/slots/copy", new { courtIds = new[] { otherClub.Courts[0].Id } });
        Assert.Equal("invalid_targets", (await JsonAsync(bad)).GetProperty("code").GetString());

        var copied = await owner.PostAsJsonAsync($"/api/courts/{source}/slots/copy", new { courtIds = new[] { club.Courts[1].Id, club.Courts[2].Id } });
        Assert.Equal(HttpStatusCode.NoContent, copied.StatusCode);
        var target = await owner.GetFromJsonAsync<JsonElement>($"/api/courts/{club.Courts[2].Id}/slots");
        Assert.Equal(50000m, target.EnumerateArray().Single().GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Blocks_CreateForSeveralCourts_ConflictWithBookings_Delete()
    {
        var (club, owner, staff) = await ClubAsync(2);
        var court1 = club.Courts[0].Id;
        var court2 = club.Courts[1].Id;
        object Block(params Guid[] courts) => new
        {
            courtIds = courts, startDate = Tomorrow, startTime = "08:00", endDate = Tomorrow, endTime = "12:30", reason = "Torneo",
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/blocks", Block(court1))).StatusCode);

        // Con una reserva adentro del rango: 409 con el detalle de la reserva.
        await owner.PostAsJsonAsync("/api/bookings", new { courtId = court2, date = Tomorrow, startTime = "09:30", phone = "341 555 0001", customerName = "Juan" });
        var clash = await JsonAsync(await owner.PostAsJsonAsync("/api/blocks", Block(court1, court2)));
        Assert.Equal("conflict", clash.GetProperty("code").GetString());
        Assert.Equal("Juan", clash.GetProperty("details").GetProperty("bookings")[0].GetProperty("customerName").GetString());

        var created = await JsonAsync(await owner.PostAsJsonAsync("/api/blocks", Block(court1)));
        var blockId = created[0].GetProperty("id").GetGuid();
        Assert.Equal("08:00:00", created[0].GetProperty("startTime").GetString());

        var listed = await staff.GetFromJsonAsync<JsonElement>("/api/blocks");
        Assert.Contains(listed.EnumerateArray(), b => b.GetProperty("id").GetGuid() == blockId);
        var agenda = await owner.GetFromJsonAsync<JsonElement>($"/api/agenda?date={Tomorrow}");
        var c1 = agenda.GetProperty("courts").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == court1);
        Assert.Equal("Block", c1.GetProperty("items")[0].GetProperty("kind").GetString());

        // Otro club no puede borrarlo.
        var (_, otherOwner, _) = await ClubAsync(1);
        Assert.Equal(HttpStatusCode.NotFound, (await otherOwner.DeleteAsync($"/api/blocks/{blockId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/blocks/{blockId}")).StatusCode);
        agenda = await owner.GetFromJsonAsync<JsonElement>($"/api/agenda?date={Tomorrow}");
        c1 = agenda.GetProperty("courts").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == court1);
        Assert.Equal("Free", c1.GetProperty("items")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task OtherClubsCourts_AreNotFound()
    {
        var (clubA, _, _) = await ClubAsync(1);
        var (_, ownerB, _) = await ClubAsync(1);

        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/api/courts/{clubA.Courts[0].Id}/slots")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.PutAsJsonAsync($"/api/courts/{clubA.Courts[0].Id}",
            new { name = "Hackeada", sport = "Padel", isCovered = false, isActive = false })).StatusCode);
    }
}
