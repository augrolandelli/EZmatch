using System.Text;
using EZmatchApi.Common;
using EZmatchApi.Data;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Services;

public interface IBotService
{
    /// <summary>Datos del club para el system prompt: fecha actual, próximos días, canchas, precios y políticas.</summary>
    Task<BotContextDto> GetContextAsync(int inboxId, CancellationToken ct = default);

    Task<BotAvailabilityDto> GetAvailabilityAsync(int inboxId, string? date, string? sport, string? from, string? to, CancellationToken ct = default);

    Task<BotBookingResultDto> CreateBookingAsync(BotCreateBookingRequest request, CancellationToken ct = default);

    Task<BotBookingsDto> GetBookingsAsync(int inboxId, string phone, CancellationToken ct = default);

    Task<BotBookingResultDto> CancelBookingAsync(Guid bookingId, BotCancelBookingRequest request, CancellationToken ct = default);
}

/// <summary>
/// Adaptador entre el agente de IA (n8n) y los servicios de reservas: resuelve el club por inbox de
/// Chatwoot, interpreta entradas en lenguaje natural y devuelve textos listos para leer (spec §6.1).
/// </summary>
public class BotService(
    EZmatchDbContext db,
    IAvailabilityService availability,
    IBookingService bookings,
    TimeProvider time) : IBotService
{
    private const int UpcomingDaysInContext = 7;

    /// <inheritdoc />
    public async Task<BotContextDto> GetContextAsync(int inboxId, CancellationToken ct = default)
    {
        var club = await db.Clubs.AsNoTracking()
            .Include(c => c.Courts.Where(co => co.IsActive).OrderBy(co => co.SortOrder))
            .ThenInclude(co => co.SlotTemplates)
            .FirstOrDefaultAsync(c => c.ChatwootInboxId == inboxId && c.IsActive, ct)
            ?? throw ClubNotFound();

        var (today, nowLocal) = LocalNow(club);

        var sports = club.Courts
            .GroupBy(c => c.Sport)
            .Select(g =>
            {
                var templates = g.SelectMany(c => c.SlotTemplates).ToList();
                return new BotSportDto(
                    g.Key.ToString(),
                    BotText.SportLabel(g.Key),
                    g.Select(c => $"{c.Name} ({BotText.Covered(c.IsCovered)})").ToList(),
                    templates.Select(t => t.DurationMinutes).Distinct().Order().ToList(),
                    templates.Count == 0 ? 0 : templates.Min(t => t.Price),
                    templates.Count == 0 ? 0 : templates.Max(t => t.Price));
            })
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Club: {club.Name}");
        if (club.Address is not null) sb.AppendLine($"Dirección: {club.Address}");
        if (club.Phone is not null) sb.AppendLine($"Teléfono del club: {club.Phone}");
        sb.AppendLine($"Fecha y hora actual: {BotText.Day(today)}/{today.Year} {BotText.Time(nowLocal)}");
        sb.AppendLine("Próximos días (usar la fecha AAAA-MM-DD en las herramientas):");
        for (var i = 0; i < UpcomingDaysInContext; i++)
        {
            var day = today.AddDays(i);
            sb.AppendLine($"- {BotText.RelativeDay(day, today)} = {BotText.Iso(day)}");
        }
        sb.AppendLine("Deportes y canchas:");
        foreach (var s in sports)
        {
            var durations = string.Join(" o ", s.DurationsMinutes.Select(d => $"{d} min"));
            var price = s.MinPrice == s.MaxPrice
                ? BotText.Money(s.MinPrice)
                : $"{BotText.Money(s.MinPrice)} a {BotText.Money(s.MaxPrice)} según el horario";
            sb.AppendLine($"- {s.Label} (valor para herramientas: \"{s.Sport}\"): {string.Join(", ", s.Courts)}. Turnos de {durations}. Precio por turno: {price}.");
        }
        sb.AppendLine("Políticas:");
        sb.AppendLine("- El turno se paga en el club al terminar. No se pide seña.");
        sb.AppendLine($"- Por WhatsApp se reserva con al menos {club.MinLeadMinutes} minutos y hasta {club.BookingHorizonDays} días de anticipación.");
        sb.AppendLine($"- Máximo {club.MaxActiveBookingsPerCustomer} reservas activas por persona por WhatsApp.");
        sb.AppendLine($"- Por WhatsApp se cancela con al menos {club.CancellationMinHours} horas de anticipación; si falta menos, derivar a una persona del club.");
        if (!string.IsNullOrWhiteSpace(club.BotInstructions))
        {
            sb.AppendLine("Información adicional del club:");
            sb.AppendLine(club.BotInstructions.Trim());
        }

        return new BotContextDto(club.Id, club.Name, BotText.Iso(today), BotText.Time(nowLocal), sports, sb.ToString().ReplaceLineEndings("\n").TrimEnd());
    }

    /// <inheritdoc />
    public async Task<BotAvailabilityDto> GetAvailabilityAsync(
        int inboxId, string? date, string? sport, string? from, string? to, CancellationToken ct = default)
    {
        var club = await ResolveClubAsync(inboxId, ct);
        var (today, _) = LocalNow(club);
        var day = BotText.ParseDate(date, today);
        var parsedSport = string.IsNullOrWhiteSpace(sport) ? (Sport?)null : BotText.ParseSport(sport, await ClubSportsAsync(club.Id, ct));
        var fromTime = BotText.ParseOptionalTime(from, "Hora desde");
        var toTime = BotText.ParseOptionalTime(to, "Hora hasta");

        var slots = await availability.GetAvailableAsync(
            club.Id, new AvailabilityQuery(day, parsedSport, fromTime, toTime), enforceBookingWindow: true, ct);

        var dayLabel = $"{BotText.RelativeDay(day, today)} ({BotText.Iso(day)})";
        string summary;
        if (slots.Count == 0)
        {
            var what = parsedSport is null ? "turnos libres" : $"turnos libres de {BotText.SportLabel(parsedSport.Value)}";
            var range = fromTime is null && toTime is null ? "" : " en ese horario";
            summary = $"No hay {what} {dayLabel}{range}.";
        }
        else
        {
            var sb = new StringBuilder();
            foreach (var group in slots.GroupBy(s => s.Sport))
            {
                sb.AppendLine($"{Capitalize(BotText.SportLabel(group.Key))} — {dayLabel}:");
                foreach (var slot in group) sb.AppendLine("• " + SlotLine(slot));
            }
            summary = sb.ToString().ReplaceLineEndings("\n").TrimEnd();
        }

        return new BotAvailabilityDto(BotText.Iso(day), slots, summary);
    }

    /// <inheritdoc />
    public async Task<BotBookingResultDto> CreateBookingAsync(BotCreateBookingRequest request, CancellationToken ct = default)
    {
        var club = await ResolveClubAsync(request.InboxId, ct);
        var (today, _) = LocalNow(club);
        var create = new CreateBookingRequest(
            BotText.ParseSport(request.Sport, await ClubSportsAsync(club.Id, ct)),
            BotText.ParseDate(request.Date, today),
            BotText.ParseTime(request.StartTime),
            request.Phone,
            request.CustomerName,
            request.CourtId);

        BookingDto booking;
        try
        {
            booking = await bookings.CreateAsync(club.Id, create, BookingSource.WhatsApp, ct);
        }
        catch (AppException ex) when (ex.Details is BookingAlternatives { Alternatives: var alternatives })
        {
            // Se reescribe el mensaje para que el bot pueda ofrecer opciones sin otra llamada.
            var options = alternatives.Count == 0
                ? $" No quedan otros turnos libres ese día."
                : " Opciones cercanas: " + string.Join("; ", alternatives.Select(SlotLine)) + ".";
            throw new AppException(ex.Message + options, ex.StatusCode, ex.Code) { Details = ex.Details };
        }

        var summary =
            $"Reserva confirmada: {BotText.SportLabel(booking.Sport)}, {BotText.RelativeDay(booking.Date, today)} " +
            $"de {BotText.Time(booking.StartTime)} a {BotText.Time(booking.EndTime)} en {booking.CourtName}, " +
            $"a nombre de {booking.CustomerName}. Precio: {BotText.Money(booking.Price)} (se paga en el club).";
        return new BotBookingResultDto(booking, summary);
    }

    /// <inheritdoc />
    public async Task<BotBookingsDto> GetBookingsAsync(int inboxId, string phone, CancellationToken ct = default)
    {
        var club = await ResolveClubAsync(inboxId, ct);
        var (today, _) = LocalNow(club);
        var upcoming = await bookings.GetUpcomingForPhoneAsync(club.Id, phone, ct);

        var summary = upcoming.Count == 0
            ? "No tiene reservas activas."
            : "Reservas activas:\n" + string.Join("\n", upcoming.Select(b =>
                $"• {BotText.SportLabel(b.Sport)}, {BotText.RelativeDay(b.Date, today)} de {BotText.Time(b.StartTime)} " +
                $"a {BotText.Time(b.EndTime)} en {b.CourtName} — {BotText.Money(b.Price)} (id: {b.Id})"));
        return new BotBookingsDto(upcoming, summary);
    }

    /// <inheritdoc />
    public async Task<BotBookingResultDto> CancelBookingAsync(Guid bookingId, BotCancelBookingRequest request, CancellationToken ct = default)
    {
        var club = await ResolveClubAsync(request.InboxId, ct);
        var (today, _) = LocalNow(club);
        var booking = await bookings.CancelAsync(club.Id, bookingId, request.Phone, BookingSource.WhatsApp, request.Reason, ct);

        var summary =
            $"Reserva cancelada: {BotText.SportLabel(booking.Sport)}, {BotText.RelativeDay(booking.Date, today)} " +
            $"de {BotText.Time(booking.StartTime)} a {BotText.Time(booking.EndTime)} en {booking.CourtName}.";
        return new BotBookingResultDto(booking, summary);
    }

    private async Task<Club> ResolveClubAsync(int inboxId, CancellationToken ct) =>
        await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.ChatwootInboxId == inboxId && c.IsActive, ct)
        ?? throw ClubNotFound();

    private async Task<IReadOnlyCollection<Sport>> ClubSportsAsync(Guid clubId, CancellationToken ct) =>
        await db.Courts.AsNoTracking()
            .Where(c => c.ClubId == clubId && c.IsActive)
            .Select(c => c.Sport)
            .Distinct()
            .ToListAsync(ct);

    private (DateOnly Today, TimeOnly Now) LocalNow(Club club)
    {
        var local = ClubTime.ToLocal(time.GetUtcNow().UtcDateTime, ClubTime.Zone(club.TimeZone));
        return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    /// <summary>"20:00 a 21:30 — 2 canchas libres (Cancha 1 techada, Cancha 3 descubierta) — $30.000".</summary>
    private static string SlotLine(AvailableSlotDto slot)
    {
        var courts = string.Join(", ", slot.Courts.Select(c => $"{c.Name} {BotText.Covered(c.IsCovered)}"));
        var count = slot.Courts.Count == 1 ? "1 cancha libre" : $"{slot.Courts.Count} canchas libres";
        var maxPrice = slot.Courts.Max(c => c.Price);
        var price = maxPrice == slot.PriceFrom
            ? BotText.Money(slot.PriceFrom)
            : $"desde {BotText.Money(slot.PriceFrom)}";
        return $"{BotText.Time(slot.StartTime)} a {BotText.Time(slot.EndTime)} — {count} ({courts}) — {price}";
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static AppException ClubNotFound() =>
        new("No hay un club activo configurado para este inbox.", StatusCodes.Status404NotFound, "club_not_found");
}
