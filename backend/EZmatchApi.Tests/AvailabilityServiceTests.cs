using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using EZmatchApi.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EZmatchApi.Tests;

[Collection(ApiCollection.Name)]
public class AvailabilityServiceTests(ApiFixture fixture)
{
    // ApiFixture.Now = lunes 2026-10-05 09:00 hora Argentina.
    private static readonly DateOnly Today = new(2026, 10, 5);
    private static readonly DateOnly Tomorrow = Today.AddDays(1);

    private Task<IReadOnlyList<AvailableSlotDto>> GetAsync(Guid clubId, AvailabilityQuery query, bool enforce = true) =>
        fixture.RunAsync(sp => sp.GetRequiredService<IAvailabilityService>().GetAvailableAsync(clubId, query, enforce));

    [Fact]
    public async Task GroupsFreeCourtsPerSlot_AndExcludesBookingsAndBlocks()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 3);
        var eightPm = new TimeOnly(20, 0);

        await fixture.RunAsync(sp => sp.GetRequiredService<IBookingService>().CreateAsync(
            club.Id, new CreateBookingRequest(Sport.Padel, Tomorrow, eightPm, "+5493415550001", "Juan"), BookingSource.Panel));
        await fixture.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            // Bloqueo de 19:00 a 21:00 local en la cancha 3: pisa el turno de 18:30 y el de 20:00.
            db.Blocks.Add(new Block
            {
                CourtId = club.Courts[2].Id, Reason = "Torneo",
                StartsAt = new DateTime(2026, 10, 6, 22, 0, 0, DateTimeKind.Utc),
                EndsAt = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        });

        var slots = await GetAsync(club.Id, new AvailabilityQuery(Tomorrow, Sport.Padel));

        Assert.Equal(11, slots.Count);
        var at20 = slots.Single(s => s.StartTime == eightPm);
        Assert.Equal(["Cancha 2"], at20.Courts.Select(c => c.Name));
        Assert.Equal(new TimeOnly(21, 30), at20.EndTime);
        Assert.Equal(30000m, at20.PriceFrom);
        Assert.Equal(["Cancha 1", "Cancha 2"], slots.Single(s => s.StartTime == new TimeOnly(18, 30)).Courts.Select(c => c.Name));
        Assert.Equal(3, slots.Single(s => s.StartTime == new TimeOnly(17, 0)).Courts.Count);
    }

    [Fact]
    public async Task FiltersByStartTimeRange()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1);

        var night = await GetAsync(club.Id, new AvailabilityQuery(Tomorrow, From: new TimeOnly(19, 0), To: new TimeOnly(22, 0)));

        Assert.Equal([new TimeOnly(20, 0), new TimeOnly(21, 30)], night.Select(s => s.StartTime));
    }

    [Fact]
    public async Task EnforcedWindow_HidesPastAndTooSoonSlots_PanelSeesAll()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 1, c => c.MinLeadMinutes = 30);

        var forBot = await GetAsync(club.Id, new AvailabilityQuery(Today));
        var forPanel = await GetAsync(club.Id, new AvailabilityQuery(Today), enforce: false);

        Assert.Equal(new TimeOnly(9, 30), forBot[0].StartTime);
        Assert.Equal(new TimeOnly(8, 0), forPanel[0].StartTime);
        Assert.Empty(await GetAsync(club.Id, new AvailabilityQuery(Today.AddDays(20))));
    }

    [Fact]
    public async Task FiltersBySport_AndIgnoresInactiveCourts()
    {
        var club = await fixture.CreateClubAsync(padelCourts: 2);
        await fixture.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<EZmatchDbContext>();
            db.Courts.Add(new Court
            {
                ClubId = club.Id, Name = "Fútbol 5", Sport = Sport.Futbol5, SortOrder = 3,
                SlotTemplates = SlotGridGenerator.Generate(new TimeOnly(9, 0), new TimeOnly(23, 0), 60, SlotGridGenerator.AllDays, _ => 50000m),
            });
            var court2 = await db.Courts.FindAsync(club.Courts[1].Id);
            court2!.IsActive = false;
            await db.SaveChangesAsync();
        });

        var futbol = await GetAsync(club.Id, new AvailabilityQuery(Tomorrow, Sport.Futbol5));
        var padel = await GetAsync(club.Id, new AvailabilityQuery(Tomorrow, Sport.Padel));

        Assert.Equal(15, futbol.Count);
        Assert.All(futbol, s => Assert.Equal(Sport.Futbol5, s.Sport));
        Assert.All(padel, s => Assert.Equal(["Cancha 1"], s.Courts.Select(c => c.Name)));
    }
}
