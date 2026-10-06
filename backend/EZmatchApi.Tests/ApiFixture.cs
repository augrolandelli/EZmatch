using EZmatchApi.Auth;
using EZmatchApi.Data;
using EZmatchApi.Models;
using EZmatchApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace EZmatchApi.Tests;

/// <summary>
/// API real + PostgreSQL real en un contenedor efímero (Testcontainers).
/// Postgres real es obligatorio: la exclusión anti-superposición no existe en SQLite/InMemory.
/// Cada test crea su propio club, así los datos no se pisan entre tests.
/// </summary>
public class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>"Ahora" fijo: lunes 2026-10-05 09:00 en Argentina (12:00 UTC).</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public const string BotKey = "test-bot-key-0123456789abcdef0123456789";

    public FakeTimeProvider Clock { get; } = new(Now);

    private static int _lastInboxId = 1000;

    /// <summary>Id de inbox de Chatwoot único por test.</summary>
    public static int NextInboxId() => Interlocked.Increment(ref _lastInboxId);

    /// <summary>Cliente HTTP con la clave del bot.</summary>
    public HttpClient BotClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Bot-Key", BotKey);
        return client;
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EZmatchDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("Bot:ApiKey", BotKey);
        builder.UseSetting("Jwt:Key", "test-jwt-key-0123456789abcdef0123456789abcdef");
        builder.UseSetting("RateLimit:LoginPerMinute", "10000");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Ejecuta <paramref name="action"/> en un scope de DI propio (como una request).</summary>
    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task RunAsync(Func<IServiceProvider, Task> action) =>
        RunAsync<object?>(async sp => { await action(sp); return null; });

    public const string DefaultPassword = "Clave1234!";

    /// <summary>Usuario del panel. Para Owner/Staff hace falta <paramref name="clubId"/>.</summary>
    public async Task<User> CreateUserAsync(UserRole role, Guid? clubId = null, string password = DefaultPassword, bool isActive = true)
    {
        var user = new User
        {
            Email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@test.local",
            FullName = $"Test {role}",
            PasswordHash = new PasswordHasher().Hash(password),
            Role = role,
            ClubId = clubId,
            IsActive = isActive,
        };
        await RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user;
    }

    /// <summary>Cliente HTTP autenticado como <paramref name="user"/> (y operando <paramref name="clubId"/> si es SuperAdmin).</summary>
    public HttpClient ClientFor(User user, Guid? clubId = null)
    {
        var client = CreateClient();
        var token = Services.GetRequiredService<ITokenService>().CreateAccessToken(user).Token;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (clubId is not null) client.DefaultRequestHeaders.Add(CurrentUser.ClubHeader, clubId.ToString());
        return client;
    }

    /// <summary>Club nuevo con <paramref name="padelCourts"/> canchas de pádel con la grilla dada (o la default).</summary>
    public async Task<Club> CreateClubAsync(
        int padelCourts = 3, Action<Club>? configure = null, Func<List<SlotTemplate>>? grid = null)
    {
        var club = new Club { Name = "Club Test", Slug = $"test-{Guid.NewGuid():N}" };
        configure?.Invoke(club);
        for (var i = 1; i <= padelCourts; i++)
        {
            club.Courts.Add(new Court
            {
                Name = $"Cancha {i}", Sport = Sport.Padel, SortOrder = i, IsCovered = i == 1,
                SlotTemplates = (grid ?? DefaultPadelGrid)(),
            });
        }

        await RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            db.Clubs.Add(club);
            await db.SaveChangesAsync();
        });
        return club;
    }

    /// <summary>Todos los días: 08:00, 09:30, 11:00 … 23:00 (90 min). $24.000, desde las 17:00 $30.000.</summary>
    public static List<SlotTemplate> DefaultPadelGrid() => SlotGridGenerator.Generate(
        new TimeOnly(8, 0), new TimeOnly(23, 0), 90, SlotGridGenerator.AllDays,
        start => start >= new TimeOnly(17, 0) ? 30000m : 24000m);
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
