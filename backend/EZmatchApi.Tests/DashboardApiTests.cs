using System.Net.Http.Json;
using System.Text.Json;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EZmatchApi.Tests;

/// <summary>Métricas de inicio con datos controlados.</summary>
[Collection(ApiCollection.Name)]
public class DashboardApiTests(ApiFixture fixture)
{
    // ApiFixture.Now = lunes 2026-10-05 09:00 hora Argentina. Período de 7 días: 29/9 al 5/10.
    private static readonly TimeZoneInfo Zone = ClubTime.Zone("America/Argentina/Buenos_Aires");

    [Fact]
    public async Task Dashboard_ComputesTodayKpisSeriesHeatmapAndTop()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);   // 11 turnos por día; desde las 17 $30.000, antes $24.000
        var court = club.Courts[0];
        await fixture.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            Customer Customer(string name, string phone, DateTime created) =>
                new() { ClubId = club.Id, Name = name, Phone = phone, CreatedAt = created };
            var ana = Customer("Ana", "+5493415550001", new DateTime(2026, 9, 20, 15, 0, 0, DateTimeKind.Utc));
            var bruno = Customer("Bruno", "+5493415550002", new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc));
            var carla = Customer("Carla", "+5493415550003", new DateTime(2026, 10, 1, 16, 0, 0, DateTimeKind.Utc));
            db.Customers.AddRange(ana, bruno, carla);

            void Book(Customer c, int month, int day, int hour, int minute, BookingSource source,
                BookingStatus status = BookingStatus.Confirmed, PaymentStatus pay = PaymentStatus.Unpaid)
            {
                var start = ClubTime.ToUtc(new DateOnly(2026, month, day), new TimeOnly(hour, minute), Zone);
                db.Bookings.Add(new Booking
                {
                    ClubId = club.Id, CourtId = court.Id, CustomerId = c.Id, StartsAt = start, EndsAt = start.AddMinutes(90),
                    Price = hour >= 17 ? 30000m : 24000m, Source = source, Status = status, PaymentStatus = pay,
                });
            }
            Book(ana, 10, 1, 20, 0, BookingSource.WhatsApp, pay: PaymentStatus.Paid);
            Book(bruno, 10, 2, 21, 30, BookingSource.Panel, BookingStatus.NoShow);
            Book(ana, 10, 5, 8, 0, BookingSource.Panel);
            Book(carla, 10, 5, 20, 0, BookingSource.WhatsApp);
            Book(carla, 10, 3, 20, 0, BookingSource.WhatsApp, BookingStatus.Cancelled);   // no cuenta
            Book(ana, 9, 25, 20, 0, BookingSource.Panel);                                  // período anterior
            await db.SaveChangesAsync();
        });
        var staff = fixture.ClientFor(await fixture.CreateUserAsync(UserRole.Staff, club.Id));

        var d = await staff.GetFromJsonAsync<JsonElement>("/api/dashboard?days=7");

        Assert.Equal("2026-09-29", d.GetProperty("from").GetString());
        Assert.Equal("2026-10-05", d.GetProperty("to").GetString());

        var today = d.GetProperty("today");
        Assert.Equal(2, today.GetProperty("bookings").GetInt32());
        Assert.Equal(54000m, today.GetProperty("pendingAmount").GetDecimal());
        Assert.Equal(Math.Round(2m / 11, 4), today.GetProperty("occupancy").GetDecimal());
        Assert.Equal("Carla", today.GetProperty("next").GetProperty("customerName").GetString());
        Assert.Equal("20:00:00", today.GetProperty("next").GetProperty("startTime").GetString());

        var k = d.GetProperty("kpis");
        decimal V(string name, string part = "value") => k.GetProperty(name).GetProperty(part).GetDecimal();
        Assert.Equal(4, V("bookings"));
        Assert.Equal(1, V("bookings", "previous"));
        Assert.Equal(Math.Round(4m / 77, 4), V("occupancy"));
        Assert.Equal(30000m, V("paidRevenue"));
        Assert.Equal(0.5m, V("whatsAppShare"));
        Assert.Equal(Math.Round(1m / 3, 4), V("noShowRate"));   // de las 3 que ya empezaron, 1 no vino
        Assert.Equal(2, V("newCustomers"));

        var daily = d.GetProperty("daily").EnumerateArray().ToList();
        Assert.Equal(7, daily.Count);
        var oct1 = daily.Single(x => x.GetProperty("date").GetString() == "2026-10-01");
        Assert.Equal(1, oct1.GetProperty("whatsApp").GetInt32());

        var thursday20 = d.GetProperty("heatmap").EnumerateArray()
            .Single(c => c.GetProperty("dayOfWeek").GetString() == "Thursday" && c.GetProperty("hour").GetInt32() == 20);
        Assert.Equal(1, thursday20.GetProperty("slots").GetInt32());
        Assert.Equal(1, thursday20.GetProperty("booked").GetInt32());

        Assert.Equal(4, d.GetProperty("courts")[0].GetProperty("booked").GetInt32());
        var top = d.GetProperty("topCustomers")[0];
        Assert.Equal("Ana", top.GetProperty("name").GetString());
        Assert.Equal(2, top.GetProperty("bookings").GetInt32());

        var bad = await staff.GetAsync("/api/dashboard?days=10");
        Assert.Equal("invalid_period", (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
