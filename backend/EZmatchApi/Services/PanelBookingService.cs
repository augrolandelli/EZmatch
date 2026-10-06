using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

public interface IPanelBookingService
{
    /// <summary>Reserva manual en un turno de la grilla de una cancha. Teléfono en formato local argentino.</summary>
    Task<BookingDto> CreateAsync(Guid clubId, PanelCreateBookingRequest request, CancellationToken ct = default);

    Task<BookingDto> SetPaymentAsync(Guid clubId, Guid bookingId, bool paid, CancellationToken ct = default);

    /// <summary>Marca o desmarca la ausencia. Solo para turnos que ya empezaron.</summary>
    Task<BookingDto> SetNoShowAsync(Guid clubId, Guid bookingId, bool noShow, CancellationToken ct = default);

    /// <summary>Clientes del club por nombre o teléfono (autocompletar).</summary>
    Task<IReadOnlyList<CustomerSummaryDto>> SearchCustomersAsync(Guid clubId, string? search, CancellationToken ct = default);
}

/// <summary>Operación diaria del mostrador sobre las reservas (spec §4.2).</summary>
public class PanelBookingService(EZmatchDbContext db, IBookingService bookings, TimeProvider time) : IPanelBookingService
{
    private const int SearchLimit = 8;

    /// <inheritdoc />
    public async Task<BookingDto> CreateAsync(Guid clubId, PanelCreateBookingRequest request, CancellationToken ct = default)
    {
        var court = await db.Courts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CourtId && c.ClubId == clubId && c.IsActive, ct)
            ?? throw AppException.NotFound("La cancha no existe.");

        var phone = PhoneNumber.NormalizeArgentine(request.Phone);
        return await bookings.CreateAsync(clubId,
            new CreateBookingRequest(court.Sport, request.Date, request.StartTime, phone, request.CustomerName, court.Id),
            BookingSource.Panel, ct);
    }

    /// <inheritdoc />
    public async Task<BookingDto> SetPaymentAsync(Guid clubId, Guid bookingId, bool paid, CancellationToken ct = default)
    {
        var (booking, zone) = await LoadActiveAsync(clubId, bookingId, ct);
        booking.PaymentStatus = paid ? PaymentStatus.Paid : PaymentStatus.Unpaid;
        await db.SaveChangesAsync(ct);
        return BookingService.ToDto(booking, zone);
    }

    /// <inheritdoc />
    public async Task<BookingDto> SetNoShowAsync(Guid clubId, Guid bookingId, bool noShow, CancellationToken ct = default)
    {
        var (booking, zone) = await LoadActiveAsync(clubId, bookingId, ct);
        if (noShow && booking.StartsAt > time.GetUtcNow().UtcDateTime)
        {
            throw new AppException("El turno todavía no empezó.", StatusCodes.Status400BadRequest, "booking_not_started");
        }
        booking.Status = noShow ? BookingStatus.NoShow : BookingStatus.Confirmed;
        await db.SaveChangesAsync(ct);
        return BookingService.ToDto(booking, zone);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerSummaryDto>> SearchCustomersAsync(Guid clubId, string? search, CancellationToken ct = default)
    {
        var text = (search ?? string.Empty).Trim();
        if (text.Length < 2) return [];

        var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return await db.Customers.AsNoTracking()
            .Where(c => c.ClubId == clubId
                && (EF.Functions.ILike(c.Name, pattern) || (digits.Length >= 3 && c.Phone.Contains(digits))))
            .OrderBy(c => c.Name)
            .Take(SearchLimit)
            .Select(c => new CustomerSummaryDto(c.Id, c.Name, c.Phone, c.IsBlocked))
            .ToListAsync(ct);
    }

    /// <summary>Reserva no cancelada del club (404 si es de otro club: no se revela que existe).</summary>
    private async Task<(Booking Booking, TimeZoneInfo Zone)> LoadActiveAsync(Guid clubId, Guid bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings
            .Include(b => b.Court)
            .Include(b => b.Customer)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.ClubId == clubId, ct)
            ?? throw AppException.NotFound("No se encontró esa reserva.");
        if (booking.Status == BookingStatus.Cancelled)
        {
            throw new AppException("La reserva está cancelada.", StatusCodes.Status409Conflict, "booking_not_active");
        }
        var timeZone = await db.Clubs.Where(c => c.Id == clubId).Select(c => c.TimeZone).FirstAsync(ct);
        return (booking, ClubTime.Zone(timeZone));
    }
}
