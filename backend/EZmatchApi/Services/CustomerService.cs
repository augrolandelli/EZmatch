using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

public interface ICustomerService
{
    /// <summary>Directorio de clientes del club con búsqueda por nombre o teléfono, orden y paginado.</summary>
    Task<CustomerPageDto> ListAsync(Guid clubId, string? search, CustomerSort sort, int page, int pageSize, CancellationToken ct = default);

    Task<CustomerDetailDto> GetAsync(Guid clubId, Guid customerId, CancellationToken ct = default);

    Task<CustomerDetailDto> UpdateAsync(Guid clubId, Guid customerId, UpdateCustomerRequest request, CancellationToken ct = default);

    /// <summary>Bloquea o desbloquea: un cliente bloqueado no puede reservar por WhatsApp (el mostrador sí).</summary>
    Task<CustomerDetailDto> SetBlockedAsync(Guid clubId, Guid customerId, bool blocked, CancellationToken ct = default);
}

/// <summary>Clientes del club (spec §3.5): historial, ausencias, notas y bloqueo.</summary>
public class CustomerService(EZmatchDbContext db, TimeProvider time) : ICustomerService
{
    private const int HistoryLimit = 100;
    public const int MaxPageSize = 100;

    /// <inheritdoc />
    public async Task<CustomerPageDto> ListAsync(
        Guid clubId, string? search, CustomerSort sort, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var now = time.GetUtcNow().UtcDateTime;

        var query = db.Customers.AsNoTracking().Where(c => c.ClubId == clubId);
        var text = (search ?? string.Empty).Trim();
        if (text.Length > 0)
        {
            var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            query = query.Where(c => EF.Functions.ILike(c.Name, pattern) || (digits.Length >= 3 && c.Phone.Contains(digits)));
        }

        var rows = query.Select(c => new
        {
            Customer = c,
            Bookings = db.Bookings.Count(b => b.CustomerId == c.Id && b.Status != BookingStatus.Cancelled),
            NoShows = db.Bookings.Count(b => b.CustomerId == c.Id && b.Status == BookingStatus.NoShow),
            Last = db.Bookings
                .Where(b => b.CustomerId == c.Id && b.Status != BookingStatus.Cancelled && b.StartsAt <= now)
                .Max(b => (DateTime?)b.StartsAt),
            Next = db.Bookings
                .Where(b => b.CustomerId == c.Id && b.Status == BookingStatus.Confirmed && b.StartsAt > now)
                .Min(b => (DateTime?)b.StartsAt),
        });

        rows = sort switch
        {
            CustomerSort.Recent => rows.OrderByDescending(r => r.Last ?? r.Customer.CreatedAt).ThenBy(r => r.Customer.Name),
            CustomerSort.NoShows => rows.OrderByDescending(r => r.NoShows).ThenBy(r => r.Customer.Name),
            _ => rows.OrderBy(r => r.Customer.Name),
        };

        var total = await query.CountAsync(ct);
        var items = await rows.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new CustomerListItemDto(
                r.Customer.Id, r.Customer.Name, r.Customer.Phone, r.Customer.IsBlocked, r.Customer.Notes,
                r.Bookings, r.NoShows, r.Last, r.Next, r.Customer.CreatedAt))
            .ToListAsync(ct);
        return new CustomerPageDto(items, total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<CustomerDetailDto> GetAsync(Guid clubId, Guid customerId, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId && c.ClubId == clubId, ct)
            ?? throw AppException.NotFound("El cliente no existe.");
        return await DetailAsync(customer, ct);
    }

    /// <inheritdoc />
    public async Task<CustomerDetailDto> UpdateAsync(Guid clubId, Guid customerId, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        var customer = await FindAsync(clubId, customerId, ct);
        customer.Name = request.Name.Trim();
        customer.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await db.SaveChangesAsync(ct);
        return await DetailAsync(customer, ct);
    }

    /// <inheritdoc />
    public async Task<CustomerDetailDto> SetBlockedAsync(Guid clubId, Guid customerId, bool blocked, CancellationToken ct = default)
    {
        var customer = await FindAsync(clubId, customerId, ct);
        customer.IsBlocked = blocked;
        await db.SaveChangesAsync(ct);
        return await DetailAsync(customer, ct);
    }

    private async Task<Customer> FindAsync(Guid clubId, Guid customerId, CancellationToken ct) =>
        await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.ClubId == clubId, ct)
        ?? throw AppException.NotFound("El cliente no existe.");

    private async Task<CustomerDetailDto> DetailAsync(Customer customer, CancellationToken ct)
    {
        var zone = ClubTime.Zone(await db.Clubs.Where(c => c.Id == customer.ClubId).Select(c => c.TimeZone).FirstAsync(ct));
        var bookings = db.Bookings.AsNoTracking().Where(b => b.CustomerId == customer.Id);

        var counts = await bookings
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Active = g.Count(b => b.Status != BookingStatus.Cancelled),
                NoShows = g.Count(b => b.Status == BookingStatus.NoShow),
                Cancelled = g.Count(b => b.Status == BookingStatus.Cancelled),
                Paid = g.Where(b => b.PaymentStatus == PaymentStatus.Paid && b.Status != BookingStatus.Cancelled).Sum(b => (decimal?)b.Price) ?? 0,
            })
            .FirstOrDefaultAsync(ct);

        var history = await bookings
            .Include(b => b.Court).Include(b => b.Customer)
            .OrderByDescending(b => b.StartsAt)
            .Take(HistoryLimit)
            .ToListAsync(ct);

        return new CustomerDetailDto(
            customer.Id, customer.Name, customer.Phone, customer.IsBlocked, customer.Notes, customer.CreatedAt,
            counts?.Active ?? 0, counts?.NoShows ?? 0, counts?.Cancelled ?? 0, counts?.Paid ?? 0,
            history.Select(b => BookingService.ToDto(b, zone)).ToList());
    }
}
