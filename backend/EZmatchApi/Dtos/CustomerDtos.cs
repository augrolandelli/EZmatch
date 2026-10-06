using FluentValidation;

namespace EZmatchApi.Dtos;

/// <summary>Cliente con sus números: reservas no canceladas, ausencias, última y próxima visita.</summary>
public record CustomerListItemDto(
    Guid Id,
    string Name,
    string Phone,
    bool IsBlocked,
    string? Notes,
    int Bookings,
    int NoShows,
    DateTime? LastBookingAt,
    DateTime? NextBookingAt,
    DateTime CreatedAt);

public record CustomerPageDto(IReadOnlyList<CustomerListItemDto> Items, int Total, int Page, int PageSize);

/// <param name="PaidAmount">Lo cobrado a este cliente (reservas marcadas como pagadas).</param>
/// <param name="History">Últimas reservas, incluidas las canceladas, de la más nueva a la más vieja.</param>
public record CustomerDetailDto(
    Guid Id,
    string Name,
    string Phone,
    bool IsBlocked,
    string? Notes,
    DateTime CreatedAt,
    int Bookings,
    int NoShows,
    int Cancellations,
    decimal PaidAmount,
    IReadOnlyList<BookingDto> History);

public enum CustomerSort
{
    Name,
    Recent,
    NoShows,
}

public record UpdateCustomerRequest(string Name, string? Notes);

public record SetBlockedRequest(bool Blocked);

public class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("El nombre es obligatorio.").MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(1000).WithMessage("Las notas no pueden pasar los 1000 caracteres.");
    }
}
