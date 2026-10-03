using EZmatchApi.Models;

namespace EZmatchApi.Dtos;

/// <summary>
/// Pedido de reserva de un turno de la grilla. Si no se indica cancha, la API asigna la primera libre.
/// </summary>
/// <param name="CustomerName">Obligatorio solo si el teléfono todavía no es cliente del club.</param>
public record CreateBookingRequest(
    Sport Sport,
    DateOnly Date,
    TimeOnly StartTime,
    string Phone,
    string? CustomerName = null,
    Guid? CourtId = null);

/// <summary>Reserva con fecha/hora expresadas en hora local del club.</summary>
public record BookingDto(
    Guid Id,
    Guid CourtId,
    string CourtName,
    Sport Sport,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateTime StartsAt,
    DateTime EndsAt,
    decimal Price,
    BookingStatus Status,
    PaymentStatus PaymentStatus,
    BookingSource Source,
    string CustomerName,
    string CustomerPhone);
