using EZmatchApi.Models;

namespace EZmatchApi.Services;

/// <summary>
/// Genera filas de grilla del tipo "de 08:00 a 23:00, cada 90 min, lun a vie".
/// Lo usa el seed y, en Fase 3, el generador del panel.
/// </summary>
public static class SlotGridGenerator
{
    /// <param name="firstStart">Hora de inicio del primer turno.</param>
    /// <param name="lastStart">Hora de inicio del último turno (inclusive). Puede ser 00:00 o más tarde si cruza la medianoche.</param>
    /// <param name="durationMinutes">Duración de cada turno.</param>
    /// <param name="days">Días de la semana a los que aplica.</param>
    /// <param name="priceFor">Precio según la hora de inicio (permite horario pico).</param>
    /// <param name="everyMinutes">Separación entre inicios; por defecto igual a la duración.</param>
    public static List<SlotTemplate> Generate(
        TimeOnly firstStart,
        TimeOnly lastStart,
        int durationMinutes,
        IEnumerable<DayOfWeek> days,
        Func<TimeOnly, decimal> priceFor,
        int? everyMinutes = null)
    {
        if (durationMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        var step = everyMinutes ?? durationMinutes;
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(everyMinutes));

        var first = (int)firstStart.ToTimeSpan().TotalMinutes;
        var last = (int)lastStart.ToTimeSpan().TotalMinutes;
        // "De 18:00 a 00:30" → el último inicio es del mismo día operativo, pasada la medianoche.
        if (last < first) last += 24 * 60;

        var result = new List<SlotTemplate>();
        foreach (var day in days.Distinct())
        {
            for (var minute = first; minute <= last; minute += step)
            {
                // Inicios pasada la medianoche pertenecen al día calendario siguiente.
                var slotDay = minute >= 24 * 60 ? (DayOfWeek)(((int)day + 1) % 7) : day;
                var start = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute % (24 * 60)));
                result.Add(new SlotTemplate
                {
                    DayOfWeek = slotDay,
                    StartTime = start,
                    DurationMinutes = durationMinutes,
                    Price = priceFor(start),
                });
            }
        }
        return result;
    }

    public static readonly DayOfWeek[] AllDays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
         DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
}
