using EZmatchApi.Common;
using EZmatchApi.Models;
using EZmatchApi.Services;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Data;

/// <summary>
/// Datos de desarrollo: club "Pádel Demo" con 3 canchas de pádel y 1 de fútbol 5,
/// y algunas reservas de hoy y mañana. Solo corre en Development y si no hay clubes.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(EZmatchDbContext db, TimeProvider time, ILogger logger)
    {
        if (await db.Clubs.AnyAsync()) return;

        var club = new Club
        {
            Name = "Pádel Demo",
            Slug = "padel-demo",
            Address = "Bv. Oroño 1234, Rosario",
            Phone = "+5493410000000",
            BotInstructions = "Se alquilan paletas a $3.000. Hay estacionamiento gratuito. Vestuarios con duchas.",
        };

        // Pádel: turnos de 90 min de 08:00 a 23:00; horario pico desde las 17:00.
        var padelGrid = () => SlotGridGenerator.Generate(
            new TimeOnly(8, 0), new TimeOnly(23, 0), 90, SlotGridGenerator.AllDays,
            start => start >= new TimeOnly(17, 0) ? 30000m : 24000m);

        // Fútbol 5: turnos de 60 min de 09:00 a 23:00; horario pico desde las 18:00.
        var futbolGrid = SlotGridGenerator.Generate(
            new TimeOnly(9, 0), new TimeOnly(23, 0), 60, SlotGridGenerator.AllDays,
            start => start >= new TimeOnly(18, 0) ? 55000m : 40000m);

        club.Courts.AddRange(
        [
            new Court { Name = "Cancha 1", Sport = Sport.Padel, IsCovered = true, SortOrder = 1, SlotTemplates = padelGrid() },
            new Court { Name = "Cancha 2", Sport = Sport.Padel, IsCovered = true, SortOrder = 2, SlotTemplates = padelGrid() },
            new Court { Name = "Cancha 3", Sport = Sport.Padel, IsCovered = false, SortOrder = 3, SlotTemplates = padelGrid() },
            new Court { Name = "Fútbol 5", Sport = Sport.Futbol5, IsCovered = false, SortOrder = 4, SlotTemplates = futbolGrid },
        ]);
        db.Clubs.Add(club);

        var customers = new[]
        {
            new Customer { ClubId = club.Id, Phone = "+5493415550001", Name = "Juan Pérez" },
            new Customer { ClubId = club.Id, Phone = "+5493415550002", Name = "Lucía Gómez" },
            new Customer { ClubId = club.Id, Phone = "+5493415550003", Name = "Martín Díaz" },
        };
        db.Customers.AddRange(customers);

        var zone = ClubTime.Zone(club.TimeZone);
        var today = DateOnly.FromDateTime(ClubTime.ToLocal(time.GetUtcNow().UtcDateTime, zone));
        var tomorrow = today.AddDays(1);

        // Algunos turnos ocupados en el horario pico para que la disponibilidad tenga algo que mostrar.
        Booking Book(int courtIndex, DateOnly date, TimeOnly start, Customer customer, BookingSource source)
        {
            var court = club.Courts[courtIndex];
            var template = court.SlotTemplates.Single(t => t.DayOfWeek == date.DayOfWeek && t.StartTime == start);
            var startsAt = ClubTime.ToUtc(date, start, zone);
            return new Booking
            {
                ClubId = club.Id, CourtId = court.Id, CustomerId = customer.Id,
                StartsAt = startsAt, EndsAt = startsAt.AddMinutes(template.DurationMinutes),
                Price = template.Price, Source = source,
            };
        }

        db.Bookings.AddRange(
            Book(0, today, new TimeOnly(20, 0), customers[0], BookingSource.WhatsApp),
            Book(1, today, new TimeOnly(20, 0), customers[1], BookingSource.Panel),
            Book(0, today, new TimeOnly(21, 30), customers[2], BookingSource.WhatsApp),
            Book(3, today, new TimeOnly(21, 0), customers[0], BookingSource.Panel),
            Book(0, tomorrow, new TimeOnly(18, 30), customers[1], BookingSource.WhatsApp));

        db.Blocks.Add(new Block
        {
            CourtId = club.Courts[2].Id,
            StartsAt = ClubTime.ToUtc(tomorrow, new TimeOnly(8, 0), zone),
            EndsAt = ClubTime.ToUtc(tomorrow, new TimeOnly(12, 30), zone),
            Reason = "Mantenimiento de césped",
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Seed de desarrollo: club {Club} creado ({ClubId})", club.Name, club.Id);
    }
}
