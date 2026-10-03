using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EZmatchApi.Common;
using EZmatchApi.Models;

namespace EZmatchApi.Services;

/// <summary>
/// Lectura tolerante de lo que manda la IA ("pádel", "20hs", "mañana") y textos en español
/// listos para que el bot los lea.
/// </summary>
public static partial class BotText
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    public static string SportLabel(Sport sport) => sport switch
    {
        Sport.Padel => "pádel",
        Sport.Futbol5 => "fútbol 5",
        Sport.Futbol7 => "fútbol 7",
        Sport.Futbol11 => "fútbol 11",
        Sport.Tenis => "tenis",
        _ => sport.ToString(),
    };

    /// <summary>
    /// "pádel", "Padel", "fútbol 5", "futbol5", "f5", "tenis"… Si dice solo "fútbol" y el club tiene
    /// un único tipo de fútbol, se elige ese. Vacío vale si el club tiene un solo deporte.
    /// </summary>
    public static Sport ParseSport(string? raw, IReadOnlyCollection<Sport> clubSports)
    {
        var key = Simplify(raw);
        Sport? sport = key switch
        {
            "padel" or "paddle" => Sport.Padel,
            "futbol5" or "f5" => Sport.Futbol5,
            "futbol7" or "f7" => Sport.Futbol7,
            "futbol11" or "f11" => Sport.Futbol11,
            "tenis" or "tennis" => Sport.Tenis,
            _ => Enum.TryParse<Sport>(key, ignoreCase: true, out var parsed) ? parsed : null,
        };

        // Sin deporte y el club tiene uno solo: no hace falta preguntarlo.
        if (sport is null && key.Length == 0 && clubSports.Count == 1)
        {
            sport = clubSports.First();
        }

        if (sport is null && key is "futbol" or "football" or "soccer")
        {
            var futbol = clubSports.Where(s => s is Sport.Futbol5 or Sport.Futbol7 or Sport.Futbol11).ToList();
            if (futbol.Count == 1) sport = futbol[0];
        }

        if (sport is null || !clubSports.Contains(sport.Value))
        {
            throw new AppException(
                $"Deporte no válido: \"{raw}\". Opciones del club: {string.Join(", ", clubSports.Select(SportLabel))}.",
                StatusCodes.Status400BadRequest, "invalid_sport");
        }
        return sport.Value;
    }

    /// <summary>"2026-10-06", "hoy", "mañana" o "6/10" (año actual o siguiente).</summary>
    public static DateOnly ParseDate(string? raw, DateOnly today)
    {
        var key = Simplify(raw);
        if (key == "hoy") return today;
        if (key is "manana") return today.AddDays(1);
        if (key is "pasadomanana") return today.AddDays(2);

        if (DateOnly.TryParseExact(raw?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return iso;
        }

        var dayMonth = DayMonthRegex().Match(raw ?? string.Empty);
        if (dayMonth.Success
            && int.TryParse(dayMonth.Groups[1].Value, out var day)
            && int.TryParse(dayMonth.Groups[2].Value, out var month)
            && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(today.Year, month))
        {
            var date = new DateOnly(today.Year, month, day);
            return date < today ? date.AddYears(1) : date;
        }

        throw new AppException($"Fecha no válida: \"{raw}\". Usar formato AAAA-MM-DD.",
            StatusCodes.Status400BadRequest, "invalid_date");
    }

    /// <summary>"20:00", "20", "20hs", "20.30", "8:30 pm".</summary>
    public static TimeOnly ParseTime(string? raw, string field = "hora")
    {
        var match = TimeRegex().Match(raw?.Trim().ToLowerInvariant() ?? string.Empty);
        if (match.Success)
        {
            var hour = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minute = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            if (match.Groups[3].Value == "pm" && hour < 12) hour += 12;
            if (match.Groups[3].Value == "am" && hour == 12) hour = 0;
            if (hour == 24) hour = 0;
            if (hour is >= 0 and < 24 && minute is >= 0 and < 60) return new TimeOnly(hour, minute);
        }

        throw new AppException($"{field} no válida: \"{raw}\". Usar formato HH:mm.",
            StatusCodes.Status400BadRequest, "invalid_time");
    }

    public static TimeOnly? ParseOptionalTime(string? raw, string field) =>
        string.IsNullOrWhiteSpace(raw) ? null : ParseTime(raw, field);

    /// <summary>"martes 6/10".</summary>
    public static string Day(DateOnly date) => $"{Es.DateTimeFormat.GetDayName(date.DayOfWeek)} {date.Day}/{date.Month}";

    /// <summary>"hoy martes 6/10", "mañana miércoles 7/10" o "jueves 8/10".</summary>
    public static string RelativeDay(DateOnly date, DateOnly today) =>
        date == today ? $"hoy {Day(date)}"
        : date == today.AddDays(1) ? $"mañana {Day(date)}"
        : Day(date);

    public static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Money(decimal amount) => "$" + amount.ToString("N0", Es);

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Covered(bool isCovered) => isCovered ? "techada" : "descubierta";

    /// <summary>Minúsculas, sin acentos ni espacios/guiones: "Fútbol 5" → "futbol5".</summary>
    private static string Simplify(string? raw)
    {
        var decomposed = (raw ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is ' ' or '-' or '_' or '.') continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"^\s*(\d{1,2})[/\-](\d{1,2})\s*$")]
    private static partial Regex DayMonthRegex();

    [GeneratedRegex(@"^(\d{1,2})(?:\s*[:.h]\s*(\d{2}))?\s*(?:hs|h|horas?)?\s*(am|pm)?$")]
    private static partial Regex TimeRegex();
}
