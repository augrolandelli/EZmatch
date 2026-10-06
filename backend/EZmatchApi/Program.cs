using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using EZmatchApi.Auth;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Models;
using EZmatchApi.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logging estructurado (Serilog)
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Enums como texto ("Padel", "Confirmed"): más legibles para el panel y para la IA.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Base de datos (PostgreSQL via EF Core Code-First, tablas/columnas en snake_case)
builder.Services.AddDbContext<EZmatchDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .UseSnakeCaseNamingConvention());

// Clave del bot (n8n → /api/bot)
var botSettings = builder.Configuration.GetSection(BotSettings.SectionName).Get<BotSettings>() ?? new BotSettings();
if (botSettings.ApiKey.Length < 32)
{
    throw new InvalidOperationException(
        "Bot:ApiKey no configurada o insegura (mínimo 32 caracteres). Usar user-secrets o variables de entorno.");
}
builder.Services.Configure<BotSettings>(builder.Configuration.GetSection(BotSettings.SectionName));
builder.Services.AddScoped<BotKeyAuthFilter>();

// Autenticación JWT del panel
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
if (jwtSettings.Key.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key no configurada o insegura (mínimo 32 caracteres). Usar user-secrets o variables de entorno.");
}
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Claims cortos tal cual se emiten ("sub", "role", "club_id"), sin el mapeo legacy de Microsoft.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            NameClaimType = EzClaims.Name,
            RoleClaimType = EzClaims.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

// Autorización por rol — el servidor siempre valida
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.ClubStaff, p => p.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Staff), nameof(UserRole.SuperAdmin)))
    .AddPolicy(Policies.ClubOwner, p => p.RequireRole(nameof(UserRole.Owner), nameof(UserRole.SuperAdmin)))
    .AddPolicy(Policies.SuperAdmin, p => p.RequireRole(nameof(UserRole.SuperAdmin)));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();

// CORS: restringido al origen del panel (ver appsettings)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// Validación y servicios de aplicación
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IBotService, BotService>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // n8n llama desde una sola IP: el límite protege ante un loop del agente, no por usuario.
    options.AddPolicy("bot", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Freno a fuerza bruta en login/refresh: por defecto 10 intentos por minuto por IP.
    var loginPerMinute = builder.Configuration.GetValue("RateLimit:LoginPerMinute", 10);
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Aplicar migraciones pendientes al arrancar (dev y prod).
// Para la beta esto simplifica el deploy; a largo plazo conviene aplicarlas en un job controlado.
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    await app.ApplyMigrationsAsync();
}

// Club demo: siempre en desarrollo; en producción solo con Seed:DemoClub=true (pruebas antes del panel).
if (app.Environment.IsDevelopment() || (bool.TryParse(app.Configuration["Seed:DemoClub"], out var seedDemo) && seedDemo))
{
    using var scope = app.Services.CreateScope();
    await DbSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<EZmatchDbContext>(),
        scope.ServiceProvider.GetRequiredService<TimeProvider>(),
        int.TryParse(app.Configuration["Seed:ChatwootInboxId"], out var inboxId) ? inboxId : null,
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>());
}

// Usuarios iniciales (SuperAdmin y dueño del club demo) si están configurados.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    await UserSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<EZmatchDbContext>(),
        scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
        app.Configuration,
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>());
}

// Detrás del proxy de Easypanel (Traefik) y, para el panel, también de nginx: la IP real del cliente
// llega en X-Forwarded-For. La API solo es alcanzable a través de esos proxies (red interna de Docker),
// así que se confía en ellos; Traefik reescribe ese header, por lo que un cliente no puede falsificarlo.
// Sin esto, el rate limit por IP vería siempre la IP del proxy y sería global para todos los usuarios.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
};
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

// Para tests de integración (WebApplicationFactory)
public partial class Program;
