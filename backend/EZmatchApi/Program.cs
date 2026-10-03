using EZmatchApi.Common;
using EZmatchApi.Data;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logging estructurado (Serilog)
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddControllers();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Base de datos (PostgreSQL via EF Core Code-First, tablas/columnas en snake_case)
builder.Services.AddDbContext<EZmatchDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .UseSnakeCaseNamingConvention());

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

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

// Detrás de un proxy reverso (Easy Panel), respetar los headers X-Forwarded-*.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseCors();

app.MapControllers();

app.Run();

// Para tests de integración (WebApplicationFactory)
public partial class Program;
