using EZmatchApi.Models;

namespace EZmatchApi.Dtos;

/// <summary>Consulta de turnos libres para una fecha local del club.</summary>
/// <param name="From">Hora mínima de inicio (inclusive), ej. 19:00 para "a la noche".</param>
/// <param name="To">Hora máxima de inicio (inclusive).</param>
public record AvailabilityQuery(DateOnly Date, Sport? Sport = null, TimeOnly? From = null, TimeOnly? To = null);

/// <summary>Un horario libre, agrupando todas las canchas libres de ese deporte en ese horario.</summary>
public record AvailableSlotDto(
    Sport Sport,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int DurationMinutes,
    DateTime StartsAt,
    decimal PriceFrom,
    IReadOnlyList<FreeCourtDto> Courts);

public record FreeCourtDto(Guid Id, string Name, bool IsCovered, decimal Price);
