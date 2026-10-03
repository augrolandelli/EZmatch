using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using EZmatchApi.Auth;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Services;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
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

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // n8n llama desde una sola IP: el límite protege ante un loop del agente, no por usuario.
    options.AddPolicy("bot", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

// Detrás de un proxy reverso (Easy Panel), respetar los headers X-Forwarded-*.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseCors();
app.UseRateLimiter();

app.MapControllers();

app.Run();

// Para tests de integración (WebApplicationFactory)
public partial class Program;
