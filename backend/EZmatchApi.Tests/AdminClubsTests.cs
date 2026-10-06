using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EZmatchApi.Data;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EZmatchApi.Tests;

/// <summary>Alta y administración de clubes por el SuperAdmin.</summary>
[Collection(ApiCollection.Name)]
public class AdminClubsTests(ApiFixture fixture)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<HttpClient> AdminAsync() => fixture.ClientFor(await fixture.CreateUserAsync(UserRole.SuperAdmin));

    private static object NewClub(string name, string email, int? inbox = null) => new
    {
        name, chatwootAccountId = inbox is null ? (int?)null : 2, chatwootInboxId = inbox,
        ownerFullName = "Dueño Nuevo", ownerEmail = email, ownerPassword = "Duenio12345",
    };

    [Fact]
    public async Task Create_ClubWithOwner_UniqueSlug_EmailAndInboxChecks()
    {
        var admin = await AdminAsync();
        var email = $"duenio-{Guid.NewGuid():N}@test.local";
        var inbox = ApiFixture.NextInboxId();
        var name = $"Pádel Norte {Guid.NewGuid():N}"[..20];

        var created = await JsonAsync(await admin.PostAsJsonAsync("/api/admin/clubs", NewClub(name, email, inbox)));
        Assert.StartsWith("padel-norte", created.GetProperty("slug").GetString());
        Assert.Equal(1, created.GetProperty("activeOwners").GetInt32());

        var again = await JsonAsync(await admin.PostAsJsonAsync("/api/admin/clubs", NewClub(name, $"otro-{Guid.NewGuid():N}@test.local")));
        Assert.Equal(created.GetProperty("slug").GetString() + "-2", again.GetProperty("slug").GetString());

        var dupEmail = await admin.PostAsJsonAsync("/api/admin/clubs", NewClub("Otro club", email));
        Assert.Equal("email_taken", (await JsonAsync(dupEmail)).GetProperty("code").GetString());
        var dupInbox = await admin.PostAsJsonAsync("/api/admin/clubs", NewClub("Otro club", $"x-{Guid.NewGuid():N}@test.local", inbox));
        Assert.Equal("inbox_taken", (await JsonAsync(dupInbox)).GetProperty("code").GetString());

        // El dueño entra a su club recién creado, sin canchas todavía.
        var login = await JsonAsync(await fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "Duenio12345" }));
        Assert.Equal(name, login.GetProperty("user").GetProperty("clubName").GetString());

        // El bot ya lo reconoce por el inbox.
        var context = await fixture.BotClient().GetFromJsonAsync<JsonElement>($"/api/bot/context?inboxId={inbox}");
        Assert.Equal(name, context.GetProperty("clubName").GetString());
    }

    [Fact]
    public async Task Update_LinksInbox_AndDeactivatingStopsBotAndLogin()
    {
        var admin = await AdminAsync();
        var club = await fixture.CreateClubAsync(padelCourts: 1, c => c.Name = "Club a vincular");
        await fixture.CreateUserAsync(UserRole.Owner, club.Id);
        var inbox = ApiFixture.NextInboxId();

        var linked = await JsonAsync(await admin.PutAsJsonAsync($"/api/admin/clubs/{club.Id}",
            new { name = "Club vinculado", isActive = true, chatwootAccountId = 3, chatwootInboxId = inbox }));
        Assert.Equal(inbox, linked.GetProperty("chatwootInboxId").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await fixture.BotClient().GetAsync($"/api/bot/context?inboxId={inbox}")).StatusCode);

        await admin.PutAsJsonAsync($"/api/admin/clubs/{club.Id}", new { name = "Club vinculado", isActive = false, chatwootAccountId = 3, chatwootInboxId = inbox });
        var bot = await fixture.BotClient().GetAsync($"/api/bot/context?inboxId={inbox}");
        Assert.Equal("club_not_found", (await JsonAsync(bot)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ClearActivity_RequiresExactName_KeepsCourtsAndGrid()
    {
        var admin = await AdminAsync();
        var club = await fixture.CreateClubAsync(padelCourts: 1, c => c.Name = "Club con prueba");
        var owner = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Owner, club.Id));
        await owner.PostAsJsonAsync("/api/bookings", new { courtId = club.Courts[0].Id, date = "2026-10-06", startTime = "20:00", phone = "341 555 0001", customerName = "Juan" });

        var wrong = await admin.PostAsJsonAsync($"/api/admin/clubs/{club.Id}/clear-activity", new { confirmName = "club con prueba" });
        Assert.Equal("confirm_mismatch", (await JsonAsync(wrong)).GetProperty("code").GetString());

        var cleared = await JsonAsync(await admin.PostAsJsonAsync($"/api/admin/clubs/{club.Id}/clear-activity", new { confirmName = "Club con prueba" }));
        Assert.Equal(1, cleared.GetProperty("bookings").GetInt32());
        Assert.Equal(1, cleared.GetProperty("customers").GetInt32());

        var agenda = await owner.GetFromJsonAsync<JsonElement>("/api/agenda?date=2026-10-06");
        Assert.Equal(11, agenda.GetProperty("courts")[0].GetProperty("items").GetArrayLength());   // todos libres de nuevo
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/customers/directory")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task OwnerCannotUseAdminEndpoints()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var owner = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Owner, club.Id));

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/admin/clubs", NewClub("X", "x@test.local"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/admin/clubs/{club.Id}/clear-activity", new { confirmName = "x" })).StatusCode);
    }

    [Fact]
    public async Task Seeder_DoesNotCrash_WhenInboxBelongsToAnotherClub()
    {
        var inbox = ApiFixture.NextInboxId();
        await fixture.CreateClubAsync(padelCourts: 1, c => c.ChatwootInboxId = inbox);
        await fixture.CreateClubAsync(padelCourts: 1, c => c.Slug = DbSeeder.DemoSlug);

        await fixture.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            await DbSeeder.SeedAsync(db, sp.GetRequiredService<TimeProvider>(), inbox, NullLogger.Instance);
            Assert.Null(await db.Clubs.Where(c => c.Slug == DbSeeder.DemoSlug).Select(c => c.ChatwootInboxId).SingleAsync());
        });
    }
}
