using EZmatchApi.Auth;
using EZmatchApi.Models;
using EZmatchApi.Services;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Data;

/// <summary>
/// Usuarios iniciales, solo si están configurados (las contraseñas vienen de variables de entorno
/// y nunca se loguean):
/// - <c>AdminSeed:Email/Password</c>: SuperAdmin, si todavía no existe ninguno.
/// - <c>Seed:DemoOwnerEmail/Password</c>: dueño del club demo, si ese email no existe.
/// </summary>
public static class UserSeeder
{
    public static async Task SeedAsync(EZmatchDbContext db, IPasswordHasher hasher, IConfiguration config, ILogger logger)
    {
        var adminEmail = config["AdminSeed:Email"];
        var adminPassword = config["AdminSeed:Password"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword)
            && !await db.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin))
        {
            db.Users.Add(new User
            {
                Email = AuthService.NormalizeEmail(adminEmail),
                FullName = config["AdminSeed:FullName"] ?? "Admin EZmatch",
                PasswordHash = hasher.Hash(adminPassword),
                Role = UserRole.SuperAdmin,
            });
            logger.LogInformation("SuperAdmin creado: {Email}", adminEmail);
        }

        var ownerEmail = config["Seed:DemoOwnerEmail"];
        var ownerPassword = config["Seed:DemoOwnerPassword"];
        if (!string.IsNullOrWhiteSpace(ownerEmail) && !string.IsNullOrWhiteSpace(ownerPassword))
        {
            var normalized = AuthService.NormalizeEmail(ownerEmail);
            var demoClubId = await db.Clubs.Where(c => c.Slug == DbSeeder.DemoSlug).Select(c => (Guid?)c.Id).FirstOrDefaultAsync();
            if (demoClubId is not null && !await db.Users.AnyAsync(u => u.Email == normalized))
            {
                db.Users.Add(new User
                {
                    Email = normalized,
                    FullName = "Dueño Pádel Demo",
                    PasswordHash = hasher.Hash(ownerPassword),
                    Role = UserRole.Owner,
                    ClubId = demoClubId,
                });
                logger.LogInformation("Dueño del club demo creado: {Email}", normalized);
            }
        }

        await db.SaveChangesAsync();
    }
}
