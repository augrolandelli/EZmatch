using EZmatchApi.Common;
using EZmatchApi.Services;

namespace EZmatchApi.Tests;

/// <summary>Tests puros (sin base) del generador de grilla y la normalización de teléfonos.</summary>
public class SlotGridAndPhoneTests
{
    [Fact]
    public void Generate_EveryNinetyMinutes_IncludesLastStart()
    {
        var grid = SlotGridGenerator.Generate(new TimeOnly(8, 0), new TimeOnly(23, 0), 90, [DayOfWeek.Monday], _ => 1m);

        Assert.Equal(11, grid.Count);
        Assert.Equal(new TimeOnly(8, 0), grid[0].StartTime);
        Assert.Equal(new TimeOnly(23, 0), grid[^1].StartTime);
    }

    [Fact]
    public void Generate_PastMidnight_MovesStartsToNextCalendarDay()
    {
        var grid = SlotGridGenerator.Generate(new TimeOnly(22, 0), new TimeOnly(1, 0), 60, [DayOfWeek.Saturday], _ => 1m);

        Assert.Equal(
            [(DayOfWeek.Saturday, new TimeOnly(22, 0)), (DayOfWeek.Saturday, new TimeOnly(23, 0)),
             (DayOfWeek.Sunday, new TimeOnly(0, 0)), (DayOfWeek.Sunday, new TimeOnly(1, 0))],
            grid.Select(s => (s.DayOfWeek, s.StartTime)));
    }

    [Fact]
    public void Generate_AppliesPeakPrice()
    {
        var grid = SlotGridGenerator.Generate(new TimeOnly(16, 0), new TimeOnly(18, 0), 60, [DayOfWeek.Monday],
            start => start >= new TimeOnly(17, 0) ? 30000m : 24000m);

        Assert.Equal([24000m, 30000m, 30000m], grid.Select(s => s.Price));
    }

    [Theory]
    [InlineData("+54 9 341 555-0001", "+5493415550001")]
    [InlineData("5493415550001", "+5493415550001")]
    [InlineData("(+54) 9341 5550001", "+5493415550001")]
    public void Phone_NormalizesToE164(string raw, string expected) =>
        Assert.Equal(expected, PhoneNumber.Normalize(raw));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1234")]
    [InlineData("+1234567890123456")]
    public void Phone_RejectsInvalid(string? raw) =>
        Assert.Equal("invalid_phone", Assert.Throws<AppException>(() => PhoneNumber.Normalize(raw)).Code);
}
