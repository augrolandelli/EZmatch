using EZmatchApi.Common;
using EZmatchApi.Models;
using EZmatchApi.Services;

namespace EZmatchApi.Tests;

/// <summary>Lectura tolerante de lo que manda la IA.</summary>
public class BotTextTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);
    private static readonly Sport[] ClubSports = [Sport.Padel, Sport.Futbol5];

    [Theory]
    [InlineData("20:00", 20, 0)]
    [InlineData("20", 20, 0)]
    [InlineData("20hs", 20, 0)]
    [InlineData("20 hs", 20, 0)]
    [InlineData("20.30", 20, 30)]
    [InlineData("21h30", 21, 30)]
    [InlineData("8:30 pm", 20, 30)]
    [InlineData("9am", 9, 0)]
    [InlineData("00:30", 0, 30)]
    [InlineData("24:00", 0, 0)]
    public void ParseTime_AcceptsCommonFormats(string raw, int hour, int minute) =>
        Assert.Equal(new TimeOnly(hour, minute), BotText.ParseTime(raw));

    [Theory]
    [InlineData("")]
    [InlineData("tarde")]
    [InlineData("25:00")]
    [InlineData("20:75")]
    public void ParseTime_RejectsInvalid(string raw) =>
        Assert.Equal("invalid_time", Assert.Throws<AppException>(() => BotText.ParseTime(raw)).Code);

    [Theory]
    [InlineData("2026-10-08", 2026, 10, 8)]
    [InlineData("hoy", 2026, 10, 5)]
    [InlineData("Mañana", 2026, 10, 6)]
    [InlineData("manana", 2026, 10, 6)]
    [InlineData("pasado mañana", 2026, 10, 7)]
    [InlineData("8/10", 2026, 10, 8)]
    [InlineData("3/1", 2027, 1, 3)]
    public void ParseDate_AcceptsIsoRelativeAndDayMonth(string raw, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), BotText.ParseDate(raw, Today));

    [Theory]
    [InlineData("el viernes")]
    [InlineData("31/2")]
    [InlineData(null)]
    public void ParseDate_RejectsInvalid(string? raw) =>
        Assert.Equal("invalid_date", Assert.Throws<AppException>(() => BotText.ParseDate(raw, Today)).Code);

    [Theory]
    [InlineData("pádel", Sport.Padel)]
    [InlineData("Padel", Sport.Padel)]
    [InlineData("paddle", Sport.Padel)]
    [InlineData("Fútbol 5", Sport.Futbol5)]
    [InlineData("futbol5", Sport.Futbol5)]
    [InlineData("Futbol5", Sport.Futbol5)]
    [InlineData("F5", Sport.Futbol5)]
    [InlineData("fútbol", Sport.Futbol5)] // único fútbol del club
    public void ParseSport_IsTolerant(string raw, Sport expected) =>
        Assert.Equal(expected, BotText.ParseSport(raw, ClubSports));

    [Theory]
    [InlineData("tenis")]   // el club no tiene
    [InlineData("vóley")]
    public void ParseSport_RejectsSportsTheClubDoesNotHave(string raw) =>
        Assert.Equal("invalid_sport", Assert.Throws<AppException>(() => BotText.ParseSport(raw, ClubSports)).Code);

    [Fact]
    public void ParseSport_AmbiguousFutbol_IsRejected() =>
        Assert.Throws<AppException>(() => BotText.ParseSport("fútbol", [Sport.Futbol5, Sport.Futbol7]));

    [Fact]
    public void Formats_AreArgentine()
    {
        Assert.Equal("$30.000", BotText.Money(30000m));
        Assert.Equal("martes 6/10", BotText.Day(new DateOnly(2026, 10, 6)));
        Assert.Equal("mañana martes 6/10", BotText.RelativeDay(new DateOnly(2026, 10, 6), Today));
    }
}
