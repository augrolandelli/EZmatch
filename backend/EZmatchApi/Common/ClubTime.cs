using System.Collections.Concurrent;

namespace EZmatchApi.Common;

/// <summary>
/// Conversión entre la hora local del club (grilla) y UTC (base de datos).
/// </summary>
public static class ClubTime
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Zones = new();

    /// <summary>Zona horaria IANA del club (cacheada).</summary>
    public static TimeZoneInfo Zone(string timeZoneId) =>
        Zones.GetOrAdd(timeZoneId, TimeZoneInfo.FindSystemTimeZoneById);

    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time, DateTimeKind.Unspecified), zone);

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
}
