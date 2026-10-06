using EZmatchApi.Models;
using FluentValidation;

namespace EZmatchApi.Dtos;

/// <param name="MissingDates">Próximas fechas en las que no se pudo reservar (cancha ocupada, bloqueada o turno fuera de la grilla).</param>
/// <param name="NextDate">Próxima fecha con la reserva generada y activa.</param>
public record FixedBookingDto(
    Guid Id,
    Guid CourtId,
    string CourtName,
    Sport Sport,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly? EndTime,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    string? Notes,
    bool IsActive,
    DateOnly? NextDate,
    IReadOnlyList<DateOnly> MissingDates);

/// <summary>Alta de un turno fijo. Si el teléfono no es cliente del club, el nombre es obligatorio.</summary>
public record CreateFixedBookingRequest(
    Guid CourtId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    string Phone,
    string? CustomerName,
    DateOnly? StartsOn = null,
    DateOnly? EndsOn = null,
    string? Notes = null);

public class CreateFixedBookingRequestValidator : AbstractValidator<CreateFixedBookingRequest>
{
    public CreateFixedBookingRequestValidator()
    {
        RuleFor(x => x.CourtId).NotEmpty().WithMessage("Elegí una cancha.");
        RuleFor(x => x.DayOfWeek).IsInEnum().WithMessage("Elegí un día de la semana.");
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Ingresá el teléfono.");
        RuleFor(x => x.CustomerName).MaximumLength(120).WithMessage("El nombre es demasiado largo.");
        RuleFor(x => x.Notes).MaximumLength(300).WithMessage("Las notas no pueden pasar los 300 caracteres.");
        RuleFor(x => x.EndsOn).GreaterThanOrEqualTo(x => x.StartsOn).When(x => x.StartsOn is not null && x.EndsOn is not null)
            .WithMessage("La fecha de fin tiene que ser posterior a la de inicio.");
    }
}
