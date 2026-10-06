using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EZmatchApi.Models;

namespace EZmatchApi.Tests;

/// <summary>Endpoints /api/bot por HTTP, tal como los llama n8n.</summary>
[Collection(ApiCollection.Name)]
public class BotApiTests(ApiFixture fixture)
{
    // ApiFixture.Now = lunes 2026-10-05 09:00 hora Argentina.
    private const string Phone = "+5493415550001";

    private async Task<(Club Club, int InboxId)> CreateClubAsync(int padelCourts = 3, bool showPrices = true)
    {
        var inboxId = ApiFixture.NextInboxId();
        var club = await fixture.CreateClubAsync(padelCourts, c =>
        {
            c.Name = "Pádel Test";
            c.ChatwootInboxId = inboxId;
            c.BotShowsPrices = showPrices;
        });
        return (club, inboxId);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static object BookingBody(int inboxId, string phone = Phone, string startTime = "20hs", string? name = "Juan") => new
    {
        inboxId, phone, sport = "pádel", date = "2026-10-06", startTime, customerName = name,
    };

    [Fact]
    public async Task RequiresBotKey()
    {
        var (_, inboxId) = await CreateClubAsync();
        var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/bot/context?inboxId={inboxId}")).StatusCode);

        client.DefaultRequestHeaders.Add("X-Bot-Key", "clave-equivocada-0123456789abcdef0123456789");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/bot/context?inboxId={inboxId}")).StatusCode);
    }

    [Fact]
    public async Task UnknownInbox_Returns404()
    {
        var response = await fixture.BotClient().GetAsync("/api/bot/context?inboxId=999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("club_not_found", (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Context_DescribesClubDatesSportsAndPolicies()
    {
        var (_, inboxId) = await CreateClubAsync();

        var body = await JsonAsync(await fixture.BotClient().GetAsync($"/api/bot/context?inboxId={inboxId}"));
        var summary = body.GetProperty("summary").GetString()!;

        Assert.Equal("2026-10-05", body.GetProperty("today").GetString());
        Assert.Contains("Club: Pádel Test", summary);
        Assert.Contains("Fecha y hora actual: lunes 5/10/2026 09:00", summary);
        Assert.Contains("- hoy lunes 5/10 = 2026-10-05", summary);
        Assert.Contains("- mañana martes 6/10 = 2026-10-06", summary);
        Assert.Contains("- domingo 11/10 = 2026-10-11", summary);
        Assert.Contains("- pádel (valor para herramientas: \"Padel\"): Cancha 1 (techada), Cancha 2 (descubierta), Cancha 3 (descubierta). Turnos de 90 min. Precio por turno: $24.000 a $30.000 según el horario.", summary);
        Assert.Contains("con al menos 3 horas de anticipación", summary);
    }

    [Fact]
    public async Task Availability_AcceptsNaturalInputs_AndSummarizes()
    {
        var (_, inboxId) = await CreateClubAsync();

        var response = await fixture.BotClient().GetAsync(
            $"/api/bot/availability?inboxId={inboxId}&date=mañana&sport=Pádel&from=19&to=22hs");
        var body = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-10-06", body.GetProperty("date").GetString());
        Assert.Equal(
            "Pádel — mañana martes 6/10 (2026-10-06):\n" +
            "• 20:00 a 21:30 — 3 canchas libres (Cancha 1 techada, Cancha 2 descubierta, Cancha 3 descubierta) — $30.000\n" +
            "• 21:30 a 23:00 — 3 canchas libres (Cancha 1 techada, Cancha 2 descubierta, Cancha 3 descubierta) — $30.000",
            body.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Availability_Empty_SaysSo()
    {
        var (_, inboxId) = await CreateClubAsync();

        var body = await JsonAsync(await fixture.BotClient().GetAsync(
            $"/api/bot/availability?inboxId={inboxId}&date=2026-10-06&sport=padel&from=23:30"));

        Assert.Equal("No hay turnos libres de pádel mañana martes 6/10 (2026-10-06) en ese horario.", body.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Availability_InvalidSport_ListsClubOptions()
    {
        var (_, inboxId) = await CreateClubAsync();

        var response = await fixture.BotClient().GetAsync($"/api/bot/availability?inboxId={inboxId}&date=hoy&sport=tenis");
        var body = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_sport", body.GetProperty("code").GetString());
        Assert.Contains("Opciones del club: pádel", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task BookListAndCancel_FullFlow()
    {
        var (_, inboxId) = await CreateClubAsync();
        var client = fixture.BotClient();

        var created = await client.PostAsJsonAsync("/api/bot/bookings", BookingBody(inboxId));
        var createdBody = await JsonAsync(created);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(
            "Reserva confirmada: pádel, mañana martes 6/10 de 20:00 a 21:30 en Cancha 1, a nombre de Juan. Precio: $30.000 (se paga en el club).",
            createdBody.GetProperty("summary").GetString());
        Assert.Equal("Padel", createdBody.GetProperty("booking").GetProperty("sport").GetString());
        var bookingId = createdBody.GetProperty("booking").GetProperty("id").GetGuid();

        var list = await JsonAsync(await client.GetAsync($"/api/bot/bookings?inboxId={inboxId}&phone=%2B5493415550001"));
        Assert.Contains($"(id: {bookingId})", list.GetProperty("summary").GetString());

        var cancelled = await client.PostAsJsonAsync($"/api/bot/bookings/{bookingId}/cancel", new { inboxId, phone = Phone });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.StartsWith("Reserva cancelada: pádel, mañana martes 6/10", (await JsonAsync(cancelled)).GetProperty("summary").GetString());

        var empty = await JsonAsync(await client.GetAsync($"/api/bot/bookings?inboxId={inboxId}&phone=%2B5493415550001"));
        Assert.Equal("No tiene reservas activas.", empty.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Booking_Conflict_MessageIncludesAlternatives()
    {
        var (_, inboxId) = await CreateClubAsync(padelCourts: 1);
        var client = fixture.BotClient();
        await client.PostAsJsonAsync("/api/bot/bookings", BookingBody(inboxId));

        var response = await client.PostAsJsonAsync("/api/bot/bookings", BookingBody(inboxId, "+5493415550002", "20:00", "Lucía"));
        var body = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("conflict", body.GetProperty("code").GetString());
        Assert.Equal(
            "Ese turno ya está ocupado. Opciones cercanas: " +
            "17:00 a 18:30 — 1 cancha libre (Cancha 1 techada) — $30.000; " +
            "18:30 a 20:00 — 1 cancha libre (Cancha 1 techada) — $30.000; " +
            "21:30 a 23:00 — 1 cancha libre (Cancha 1 techada) — $30.000.",
            body.GetProperty("message").GetString());
        Assert.Equal(3, body.GetProperty("details").GetProperty("alternatives").GetArrayLength());
    }

    [Fact]
    public async Task Booking_NewCustomerWithoutName_Returns422()
    {
        var (_, inboxId) = await CreateClubAsync();

        var response = await fixture.BotClient().PostAsJsonAsync("/api/bot/bookings", BookingBody(inboxId, name: null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("customer_name_required", (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PricesHidden_NoAmountAnywhereTheAiCanRead()
    {
        var (_, inboxId) = await CreateClubAsync(padelCourts: 1, showPrices: false);
        var client = fixture.BotClient();

        var context = await JsonAsync(await client.GetAsync($"/api/bot/context?inboxId={inboxId}"));
        var availability = await JsonAsync(await client.GetAsync($"/api/bot/availability?inboxId={inboxId}&date=mañana&from=20&to=20"));
        var created = await JsonAsync(await client.PostAsJsonAsync("/api/bot/bookings", BookingBody(inboxId)));
        var conflict = await client.PostAsJsonAsync("/api/bot/bookings", BookingBody(inboxId, "+5493415550002", "20:00", "Lucía"));
        var list = await JsonAsync(await client.GetAsync($"/api/bot/bookings?inboxId={inboxId}&phone=%2B5493415550001"));

        Assert.Contains("Los precios no se informan por WhatsApp", context.GetProperty("summary").GetString());
        Assert.Equal("• 20:00 a 21:30 — 1 cancha libre (Cancha 1 techada)", availability.GetProperty("summary").GetString()!.Split('\n')[1]);
        Assert.Equal(0, availability.GetProperty("slots").GetArrayLength());
        Assert.EndsWith("a nombre de Juan. Se paga en el club.", created.GetProperty("summary").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("booking").GetProperty("price").ValueKind);
        foreach (var text in new[]
        {
            context.ToString(), availability.ToString(), created.ToString(), list.ToString(), await conflict.Content.ReadAsStringAsync(),
        })
        {
            Assert.DoesNotContain("$", text);
            Assert.DoesNotContain("30000", text);
            Assert.DoesNotContain("24000", text);
        }
    }
}
