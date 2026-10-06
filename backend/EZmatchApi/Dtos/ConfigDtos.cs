using EZmatchApi.Models;
using FluentValidation;

namespace EZmatchApi.Dtos;

// ---- Club ----

public record ClubSettingsDto(
    Guid Id,
    string Name,
    string? Address,
    string? Phone,
    string TimeZone,
    string? AssistantName,
    string? BotInstructions,
    int CancellationMinHours,
    int MinLeadMinutes,
    int BookingHorizonDays,
    int MaxActiveBookingsPerCustomer,
    int? ChatwootAccountId,
    int? ChatwootInboxId);

/// <summary>Lo que el dueño puede cambiar de su club. El inbox de Chatwoot lo vincula el SuperAdmin.</summary>
public record UpdateClubSettingsRequest(
    string Name,
    string? Address,
    string? Phone,
    string? AssistantName,
    string? BotInstructions,
    int CancellationMinHours,
    int MinLeadMinutes,
    int BookingHorizonDays,
    int MaxActiveBookingsPerCustomer);

public class UpdateClubSettingsRequestValidator : AbstractValidator<UpdateClubSettingsRequest>
{
    public UpdateClubSettingsRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("El nombre del club es obligatorio.").MaximumLength(120);
        RuleFor(x => x.Address).MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.AssistantName).MaximumLength(40).WithMessage("El nombre del asistente es demasiado largo.");
        RuleFor(x => x.BotInstructions).MaximumLength(2000).WithMessage("Las instrucciones no pueden pasar los 2000 caracteres.");
        RuleFor(x => x.CancellationMinHours).InclusiveBetween(0, 168).WithMessage("La anticipación para cancelar va de 0 a 168 horas.");
        RuleFor(x => x.MinLeadMinutes).InclusiveBetween(0, 1440).WithMessage("La anticipación mínima va de 0 a 1440 minutos.");
        RuleFor(x => x.BookingHorizonDays).InclusiveBetween(1, 90).WithMessage("Se puede reservar con hasta 1 a 90 días de anticipación.");
        RuleFor(x => x.MaxActiveBookingsPerCustomer).InclusiveBetween(1, 10).WithMessage("El máximo de reservas por persona va de 1 a 10.");
    }
}

// ---- Canchas ----

public record CourtDto(Guid Id, string Name, Sport Sport, bool IsCovered, int SortOrder, bool IsActive, int SlotCount);

public record SaveCourtRequest(string Name, Sport Sport, bool IsCovered, bool IsActive);

public record ReorderCourtsRequest(IReadOnlyList<Guid> CourtIds);

public class SaveCourtRequestValidator : AbstractValidator<SaveCourtRequest>
{
    public SaveCourtRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Poné un nombre a la cancha.").MaximumLength(60);
        RuleFor(x => x.Sport).IsInEnum();
    }
}

// ---- Grilla ----

public record SlotTemplateDto(Guid Id, DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes, decimal Price);

public record SlotInput(DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes, decimal Price);

/// <summary>Reemplaza la grilla completa de la cancha.</summary>
public record ReplaceSlotsRequest(IReadOnlyList<SlotInput> Slots);

/// <summary>
/// "De <FirstStart> a <LastStart>, cada <EveryMinutes>, turnos de <DurationMinutes>, a <Price>; desde <PeakFrom>, <PeakPrice>".
/// Reemplaza los turnos de esos días (y los que se pisen con los nuevos pasada la medianoche).
/// </summary>
public record GenerateSlotsRequest(
    IReadOnlyList<DayOfWeek> Days,
    TimeOnly FirstStart,
    TimeOnly LastStart,
    int DurationMinutes,
    int? EveryMinutes,
    decimal Price,
    TimeOnly? PeakFrom,
    decimal? PeakPrice);

public record CopySlotsRequest(IReadOnlyList<Guid> CourtIds);

public class SlotInputValidator : AbstractValidator<SlotInput>
{
    public SlotInputValidator()
    {
        RuleFor(x => x.DayOfWeek).IsInEnum();
        RuleFor(x => x.DurationMinutes).InclusiveBetween(15, 480).WithMessage("La duración de un turno va de 15 a 480 minutos.");
        RuleFor(x => x.Price).InclusiveBetween(0, 100_000_000).WithMessage("El precio no es válido.");
    }
}

public class ReplaceSlotsRequestValidator : AbstractValidator<ReplaceSlotsRequest>
{
    public ReplaceSlotsRequestValidator()
    {
        RuleFor(x => x.Slots).NotNull().Must(s => s.Count <= 7 * 96).WithMessage("Demasiados turnos.");
        RuleForEach(x => x.Slots).SetValidator(new SlotInputValidator());
    }
}

public class GenerateSlotsRequestValidator : AbstractValidator<GenerateSlotsRequest>
{
    public GenerateSlotsRequestValidator()
    {
        RuleFor(x => x.Days).NotEmpty().WithMessage("Elegí al menos un día.");
        RuleForEach(x => x.Days).IsInEnum();
        RuleFor(x => x.DurationMinutes).InclusiveBetween(15, 480).WithMessage("La duración de un turno va de 15 a 480 minutos.");
        RuleFor(x => x.EveryMinutes).InclusiveBetween(15, 480).When(x => x.EveryMinutes is not null)
            .WithMessage("La separación entre turnos va de 15 a 480 minutos.");
        RuleFor(x => x.Price).InclusiveBetween(0, 100_000_000).WithMessage("El precio no es válido.");
        RuleFor(x => x.PeakPrice).NotNull().When(x => x.PeakFrom is not null).WithMessage("Indicá el precio del horario pico.");
        RuleFor(x => x.PeakPrice).InclusiveBetween(0, 100_000_000).When(x => x.PeakPrice is not null).WithMessage("El precio pico no es válido.");
    }
}

// ---- Bloqueos ----

public record BlockDto(
    Guid Id,
    Guid CourtId,
    string CourtName,
    DateTime StartsAt,
    DateTime EndsAt,
    DateOnly StartDate,
    TimeOnly StartTime,
    DateOnly EndDate,
    TimeOnly EndTime,
    string Reason);

/// <summary>Bloquea una o varias canchas en un rango de fecha/hora local del club.</summary>
public record CreateBlockRequest(
    IReadOnlyList<Guid> CourtIds,
    DateOnly StartDate,
    TimeOnly StartTime,
    DateOnly EndDate,
    TimeOnly EndTime,
    string Reason);

/// <summary>Detalle de un 409 al bloquear: reservas que ya ocupan ese horario.</summary>
public record BlockConflicts(IReadOnlyList<BookingDto> Bookings);

public class CreateBlockRequestValidator : AbstractValidator<CreateBlockRequest>
{
    public CreateBlockRequestValidator()
    {
        RuleFor(x => x.CourtIds).NotEmpty().WithMessage("Elegí al menos una cancha.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Indicá el motivo (ej. Torneo, Mantenimiento, Lluvia).").MaximumLength(200);
        RuleFor(x => x).Must(x => x.EndDate.ToDateTime(x.EndTime) > x.StartDate.ToDateTime(x.StartTime))
            .WithName("EndTime").WithMessage("El fin del bloqueo tiene que ser posterior al inicio.");
    }
}
