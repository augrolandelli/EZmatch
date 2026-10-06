using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using EZmatchApi.Auth;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EZmatchApi.Tests;

[Collection(ApiCollection.Name)]
public class AuthApiTests(ApiFixture fixture)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private Task<HttpResponseMessage> LoginAsync(string email, string password = ApiFixture.DefaultPassword) =>
        fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        fixture.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    [Fact]
    public async Task Login_ReturnsTokensAndUserWithClub_EmailIsCaseInsensitive()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1, c => c.Name = "Club Login");
        var owner = await fixture.CreateUserAsync(UserRole.Owner, club.Id);

        var response = await LoginAsync("  " + owner.Email.ToUpperInvariant() + " ");
        var body = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("refreshToken").GetString()));
        var user = body.GetProperty("user");
        Assert.Equal("Owner", user.GetProperty("role").GetString());
        Assert.Equal("Club Login", user.GetProperty("clubName").GetString());

        // El access token sirve para /me.
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("accessToken").GetString());
        var me = await JsonAsync(await client.GetAsync("/api/auth/me"));
        Assert.Equal(owner.Id, me.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_SameGenericError()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var owner = await fixture.CreateUserAsync(UserRole.Owner, club.Id);

        var wrong = await LoginAsync(owner.Email, "otra-clave-123");
        var unknown = await LoginAsync("nadie@test.local");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await JsonAsync(wrong)).GetProperty("message").GetString(),
                     (await JsonAsync(unknown)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_InactiveUserOrClub_IsForbidden()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var inactive = await fixture.CreateUserAsync(UserRole.Staff, club.Id, isActive: false);
        Assert.Equal("user_inactive", (await JsonAsync(await LoginAsync(inactive.Email))).GetProperty("code").GetString());

        var closedClub = await fixture.CreateClubAsync(padelCourts: 1, c => c.IsActive = false);
        var owner = await fixture.CreateUserAsync(UserRole.Owner, closedClub.Id);
        Assert.Equal("club_inactive", (await JsonAsync(await LoginAsync(owner.Email))).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Me_WithoutToken_Is401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.CreateClient().GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndReuseAfterGraceRevokesAllSessions()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var owner = await fixture.CreateUserAsync(UserRole.Owner, club.Id);
        var first = (await JsonAsync(await LoginAsync(owner.Email))).GetProperty("refreshToken").GetString()!;

        var rotated = await RefreshAsync(first);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = (await JsonAsync(rotated)).GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(first, second);

        // Dentro del período de gracia (otra pestaña renovando a la vez): 401 pero la sesión nueva sigue viva.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first)).StatusCode);
        var third = (await JsonAsync(await RefreshAsync(second))).GetProperty("refreshToken").GetString()!;

        // Fuera de la gracia, reusar un token rotado es sospechoso: se cierran todas las sesiones.
        // (Se "envejece" la revocación en la base: el reloj de los tests es compartido y no retrocede.)
        await fixture.RunAsync(sp =>
        {
            var hash = sp.GetRequiredService<ITokenService>().HashRefreshToken(second);
            return sp.GetRequiredService<EZmatchDbContext>().RefreshTokens
                .Where(t => t.TokenHash == hash)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, ApiFixture.Now.UtcDateTime.AddMinutes(-2)));
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(third)).StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var owner = await fixture.CreateUserAsync(UserRole.Owner, club.Id);
        var refresh = (await JsonAsync(await LoginAsync(owner.Email))).GetProperty("refreshToken").GetString()!;

        var logout = await fixture.CreateClient().PostAsJsonAsync("/api/auth/logout", new { refreshToken = refresh });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(refresh)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrent_AndClosesOtherSessions()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);
        var owner = await fixture.CreateUserAsync(UserRole.Owner, club.Id);
        var otherSession = (await JsonAsync(await LoginAsync(owner.Email))).GetProperty("refreshToken").GetString()!;
        var client = fixture.ClientFor(owner);

        var wrong = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "nope-nope", newPassword = "NuevaClave99" });
        Assert.Equal("wrong_password", (await JsonAsync(wrong)).GetProperty("code").GetString());

        var tooShort = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = ApiFixture.DefaultPassword, newPassword = "corta" });
        Assert.Equal("validation_error", (await JsonAsync(tooShort)).GetProperty("code").GetString());

        var ok = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = ApiFixture.DefaultPassword, newPassword = "NuevaClave99" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(otherSession)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(owner.Email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(owner.Email, "NuevaClave99")).StatusCode);
    }
}

/// <summary>Resolución del club de la request (aislamiento entre clubes).</summary>
public class CurrentUserTests
{
    private static CurrentUser For(UserRole role, Guid? clubClaim, string? clubHeader)
    {
        var claims = new List<Claim> { new(EzClaims.UserId, Guid.NewGuid().ToString()), new(EzClaims.Role, role.ToString()) };
        if (clubClaim is not null) claims.Add(new Claim(EzClaims.ClubId, clubClaim.ToString()!));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        if (clubHeader is not null) context.Request.Headers[CurrentUser.ClubHeader] = clubHeader;
        return new CurrentUser(new HttpContextAccessor { HttpContext = context });
    }

    [Fact]
    public void Owner_AlwaysUsesTokenClub_IgnoringHeader()
    {
        var own = Guid.NewGuid();
        var other = Guid.NewGuid();

        Assert.Equal(own, For(UserRole.Owner, own, other.ToString()).ClubId);
        Assert.Equal(own, For(UserRole.Staff, own, other.ToString()).ClubId);
    }

    [Fact]
    public void SuperAdmin_UsesHeader_AndRequiresIt()
    {
        var selected = Guid.NewGuid();

        Assert.Equal(selected, For(UserRole.SuperAdmin, null, selected.ToString()).ClubId);
        Assert.Equal("club_required", Assert.Throws<AppException>(() => For(UserRole.SuperAdmin, null, null).ClubId).Code);
    }
}

[Collection(ApiCollection.Name)]
public class AdminClubsApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task ListClubs_OnlySuperAdmin()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1, c => c.Name = "Club Listado");
        var owner = await fixture.CreateUserAsync(UserRole.Owner, club.Id);
        var admin = await fixture.CreateUserAsync(UserRole.SuperAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.ClientFor(owner).GetAsync("/api/admin/clubs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.CreateClient().GetAsync("/api/admin/clubs")).StatusCode);

        var clubs = await fixture.ClientFor(admin).GetFromJsonAsync<JsonElement>("/api/admin/clubs");
        Assert.Contains(clubs.EnumerateArray(), c => c.GetProperty("name").GetString() == "Club Listado");
    }
}
