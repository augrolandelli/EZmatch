using EZmatchApi.Models;
using FluentValidation;

namespace EZmatchApi.Dtos;

/// <summary>Agenda de un día del club: una columna por cancha, con turnos ubicados por minuto del día.</summary>
public record AgendaDto(
    DateOnly Date,
    DateOnly Today,
    IReadOnlyList<AgendaCourtDto> Courts,
    AgendaSummaryDto Summary);

public record AgendaCourtDto(Guid Id, string Name, Sport Sport, bool IsCovered, IReadOnlyList<AgendaItemDto> Items);

/// <summary>Qué ocupa un tramo de la columna de una cancha.</summary>
public enum AgendaItemKind
{
    /// <summary>Turno de la grilla sin reservar.</summary>
    Free,
    /// <summary>Reserva que empieza este día.</summary>
    Booking,
    /// <summary>Bloqueo (torneo, mantenimiento).</summary>
    Block,
    /// <summary>Turno ocupado por una reserva del día anterior que cruza la medianoche.</summary>
    Busy,
}

/// <param name="StartMinute">Minutos desde las 00:00 del día de la agenda (puede pasar de 1440 si cruza la medianoche).</param>
public record AgendaItemDto(
    AgendaItemKind Kind,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int StartMinute,
    int DurationMinutes,
    DateTime StartsAt,
    DateTime EndsAt,
    decimal? Price,
    bool IsPast,
    AgendaBookingDto? Booking,
    string? BlockReason);

public record AgendaBookingDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    bool CustomerIsBlocked,
    decimal Price,
    BookingStatus Status,
    PaymentStatus PaymentStatus,
    BookingSource Source,
    DateTime CreatedAt);

/// <param name="PendingAmount">Reservas sin pagar, excluyendo ausencias.</param>
public record AgendaSummaryDto(int Bookings, int FreeSlots, int NoShows, decimal PaidAmount, decimal PendingAmount);

/// <summary>Reserva manual desde el panel (mostrador / teléfono) en un turno de la grilla de una cancha.</summary>
public record PanelCreateBookingRequest(Guid CourtId, DateOnly Date, TimeOnly StartTime, string Phone, string? CustomerName);

public record PanelCancelBookingRequest(string? Reason);

public record SetPaymentRequest(bool Paid);

public record SetNoShowRequest(bool NoShow);

public record CustomerSummaryDto(Guid Id, string Name, string Phone, bool IsBlocked);

public class PanelCreateBookingRequestValidator : AbstractValidator<PanelCreateBookingRequest>
{
    public PanelCreateBookingRequestValidator()
    {
        RuleFor(x => x.CourtId).NotEmpty().WithMessage("Elegí una cancha.");
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Ingresá el teléfono.");
        RuleFor(x => x.CustomerName).MaximumLength(120).WithMessage("El nombre es demasiado largo.");
    }
}
