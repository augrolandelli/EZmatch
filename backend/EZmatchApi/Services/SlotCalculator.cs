using EZmatchApi.Common;
using EZmatchApi.Models;

namespace EZmatchApi.Services;

/// <summary>Un turno concreto (grilla aplicada a una fecha), con instantes en UTC.</summary>
public record SlotInstance(
    Guid CourtId,
    string CourtName,
    bool IsCovered,
    int SortOrder,
    Sport Sport,
    DateOnly Date,
    TimeOnly StartTime,
    int DurationMinutes,
    decimal Price,
    DateTime StartsAt,
    DateTime EndsAt)
{
    public bool Overlaps(DateTime start, DateTime end) => start < EndsAt && end > StartsAt;
}

/// <summary>Convierte la grilla semanal de una cancha en turnos concretos para una fecha.</summary>
public static class SlotCalculator
{
    /// <summary>
    /// Turnos de <paramref name="court"/> que empiezan en <paramref name="date"/> (hora local del club).
    /// Requiere <see cref="Court.SlotTemplates"/> cargado. Un turno de 23:30 a 01:00 pertenece al día en que empieza.
    /// </summary>
    public static IEnumerable<SlotInstance> ForDate(Court court, DateOnly date, TimeZoneInfo zone) =>
        court.SlotTemplates
            .Where(t => t.DayOfWeek == date.DayOfWeek)
            .Select(t =>
            {
                var startsAt = ClubTime.ToUtc(date, t.StartTime, zone);
                return new SlotInstance(
                    court.Id, court.Name, court.IsCovered, court.SortOrder, court.Sport,
                    date, t.StartTime, t.DurationMinutes, t.Price,
                    startsAt, startsAt.AddMinutes(t.DurationMinutes));
            });
}
