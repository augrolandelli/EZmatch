using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EZmatchApi.Models;

namespace EZmatchApi.Tests;

/// <summary>Clientes (directorio, detalle, notas, bloqueo) y usuarios del club.</summary>
[Collection(ApiCollection.Name)]
public class CustomersAndUsersApiTests(ApiFixture fixture)
{
    // ApiFixture.Now = lunes 2026-10-05 09:00 hora Argentina.
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid courtId, string date, string time, string phone, string name) =>
        client.PostAsJsonAsync("/api/bookings", new { courtId, date, startTime = time, phone, customerName = name });

    [Fact]
    public async Task Directory_ShowsStats_SearchAndSort()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var staff = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Staff, club.Id));
        var court = club.Courts[0].Id;

        // Ana: una pasada (no vino) y una futura. Bruno: una cancelada.
        var past = (await JsonAsync(await BookAsync(staff, court, "2026-10-05", "08:00", "341 555 0001", "Ana Gómez"))).GetProperty("id").GetGuid();
        await staff.PutAsJsonAsync($"/api/bookings/{past}/no-show", new { noShow = true });
        await BookAsync(staff, court, "2026-10-06", "20:00", "341 555 0001", "Ana Gómez");
        var cancelled = (await JsonAsync(await BookAsync(staff, court, "2026-10-06", "21:30", "11 4444 9999", "Bruno Díaz"))).GetProperty("id").GetGuid();
        await staff.PostAsJsonAsync($"/api/bookings/{cancelled}/cancel", new { reason = "x" });

        var page = await staff.GetFromJsonAsync<JsonElement>("/api/customers/directory");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        var ana = page.GetProperty("items")[0];
        Assert.Equal("Ana Gómez", ana.GetProperty("name").GetString());
        Assert.Equal(2, ana.GetProperty("bookings").GetInt32());
        Assert.Equal(1, ana.GetProperty("noShows").GetInt32());
        Assert.Equal(new DateTime(2026, 10, 5, 11, 0, 0, DateTimeKind.Utc), ana.GetProperty("lastBookingAt").GetDateTime());
        Assert.Equal(new DateTime(2026, 10, 6, 23, 0, 0, DateTimeKind.Utc), ana.GetProperty("nextBookingAt").GetDateTime());
        Assert.Equal(0, page.GetProperty("items")[1].GetProperty("bookings").GetInt32());

        var byPhone = await staff.GetFromJsonAsync<JsonElement>("/api/customers/directory?search=4444");
        Assert.Equal("Bruno Díaz", byPhone.GetProperty("items")[0].GetProperty("name").GetString());

        var byNoShows = await staff.GetFromJsonAsync<JsonElement>("/api/customers/directory?sort=NoShows");
        Assert.Equal("Ana Gómez", byNoShows.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Detail_History_Notes_AndBlockingStopsTheBot()
    {
        var inboxId = ApiFixture.NextInboxId();
        var club = await fixture.CreateClubAsync(padelCourts: 1, c => c.ChatwootInboxId = inboxId);
        var staff = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Staff, club.Id));
        var booking = await JsonAsync(await BookAsync(staff, club.Courts[0].Id, "2026-10-06", "17:00", "341 555 0001", "Ana"));
        await staff.PutAsJsonAsync($"/api/bookings/{booking.GetProperty("id").GetGuid()}/payment", new { paid = true });
        var customerId = (await staff.GetFromJsonAsync<JsonElement>("/api/customers/directory")).GetProperty("items")[0].GetProperty("id").GetGuid();

        var updated = await JsonAsync(await staff.PutAsJsonAsync($"/api/customers/{customerId}", new { name = "Ana Gómez", notes = "Juega los martes" }));
        Assert.Equal("Juega los martes", updated.GetProperty("notes").GetString());
        Assert.Equal(30000m, updated.GetProperty("paidAmount").GetDecimal());   // 17:00 ya es horario pico
        Assert.Equal(1, updated.GetProperty("history").GetArrayLength());

        var blocked = await JsonAsync(await staff.PutAsJsonAsync($"/api/customers/{customerId}/blocked", new { blocked = true }));
        Assert.True(blocked.GetProperty("isBlocked").GetBoolean());

        var botTry = await fixture.BotClient().PostAsJsonAsync("/api/bot/bookings", new
        {
            inboxId, phone = "+5493415550001", sport = "Padel", date = "2026-10-06", startTime = "20:00",
        });
        Assert.Equal("customer_blocked", (await JsonAsync(botTry)).GetProperty("code").GetString());

        // Otro club no lo ve.
        var other = await fixture.CreateClubAsync(padelCourts: 1);
        var otherStaff = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Staff, other.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await otherStaff.GetAsync($"/api/customers/{customerId}")).StatusCode);
    }

    [Fact]
    public async Task Users_OwnerManagesStaff_StaffCannot()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var ownerUser = await fixture.CreateUserAsync(UserRole.Owner, club.Id);
        var owner = fixture.ClientFor(ownerUser);
        var staff = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Staff, club.Id));
        var email = $"recepcion-{Guid.NewGuid():N}@test.local";

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/users")).StatusCode);

        var created = await JsonAsync(await owner.PostAsJsonAsync("/api/users", new { email, fullName = "Laura", role = "Staff", password = "Recepcion123" }));
        var lauraId = created.GetProperty("id").GetGuid();
        Assert.Equal("Staff", created.GetProperty("role").GetString());

        var dup = await owner.PostAsJsonAsync("/api/users", new { email = email.ToUpperInvariant(), fullName = "Otra", role = "Staff", password = "Recepcion123" });
        Assert.Equal("email_taken", (await JsonAsync(dup)).GetProperty("code").GetString());
        var superAdmin = await owner.PostAsJsonAsync("/api/users", new { email = "x@test.local", fullName = "X", role = "SuperAdmin", password = "Recepcion123" });
        Assert.Equal("validation_error", (await JsonAsync(superAdmin)).GetProperty("code").GetString());

        // Laura entra, el dueño le blanquea la contraseña: la sesión vieja muere y entra con la nueva.
        var login = await fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "Recepcion123" });
        var refresh = (await JsonAsync(login)).GetProperty("refreshToken").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/users/{lauraId}/reset-password", new { password = "NuevaClave456" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "NuevaClave456" })).StatusCode);

        // Desactivada no entra.
        await owner.PutAsJsonAsync($"/api/users/{lauraId}", new { fullName = "Laura", role = "Staff", isActive = false });
        var inactive = await fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "NuevaClave456" });
        Assert.Equal("user_inactive", (await JsonAsync(inactive)).GetProperty("code").GetString());

        var list = await owner.GetFromJsonAsync<JsonElement>("/api/users");
        Assert.Contains(list.EnumerateArray(), u => u.GetProperty("id").GetGuid() == ownerUser.Id && u.GetProperty("isMe").GetBoolean());
    }

    [Fact]
    public async Task Users_CannotLockTheClubOut()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var ownerUser = await fixture.CreateUserAsync(UserRole.Owner, club.Id);
        var owner = fixture.ClientFor(ownerUser);

        var self = await owner.PutAsJsonAsync($"/api/users/{ownerUser.Id}", new { fullName = "Yo", role = "Owner", isActive = false });
        Assert.Equal("cannot_change_self", (await JsonAsync(self)).GetProperty("code").GetString());

        // El SuperAdmin intenta dejar al club sin dueños activos.
        var admin = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.SuperAdmin), club.Id);
        var last = await admin.PutAsJsonAsync($"/api/users/{ownerUser.Id}", new { fullName = "Dueño", role = "Staff", isActive = true });
        Assert.Equal("last_owner", (await JsonAsync(last)).GetProperty("code").GetString());

        // Con un segundo dueño, sí se puede pasar el primero a recepción.
        var second = await JsonAsync(await admin.PostAsJsonAsync("/api/users", new
        {
            email = $"socio-{Guid.NewGuid():N}@test.local", fullName = "Socio", role = "Owner", password = "Socio12345",
        }));
        Assert.Equal("Owner", second.GetProperty("role").GetString());
        var demoted = await admin.PutAsJsonAsync($"/api/users/{ownerUser.Id}", new { fullName = "Dueño", role = "Staff", isActive = true });
        Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);

        // Otro club no puede tocar estos usuarios.
        var otherClub = await fixture.CreateClubAsync(padelCourts: 1);
        var otherOwner = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Owner, otherClub.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await otherOwner.PostAsJsonAsync($"/api/users/{ownerUser.Id}/reset-password", new { password = "Hackeo12345" })).StatusCode);
    }
}
