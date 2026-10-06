namespace EZmatchApi.Dtos;

// Contratos de /api/bot. Las entradas son strings a propósito: las completa una IA y la API
// las interpreta con tolerancia ("pádel", "20hs", "mañana"). Cada respuesta trae un "summary"
// en español listo para que el bot lo use.

/// <summary>Pedido de reserva desde n8n. <c>InboxId</c> y <c>Phone</c> los inyecta n8n, nunca la IA.</summary>
public record BotCreateBookingRequest(
    int InboxId,
    string Phone,
    string Sport,
    string Date,
    string StartTime,
    string? CustomerName = null,
    Guid? CourtId = null);

public record BotCancelBookingRequest(int InboxId, string Phone, string? Reason = null);

/// <param name="AssistantName">Nombre con el que se presenta el bot en este club (null = genérico).</param>
public record BotContextDto(
    Guid ClubId,
    string ClubName,
    string? AssistantName,
    string Today,
    string Now,
    IReadOnlyList<BotSportDto> Sports,
    string Summary);

public record BotSportDto(string Sport, string Label, IReadOnlyList<string> Courts, IReadOnlyList<int> DurationsMinutes, decimal? MinPrice, decimal? MaxPrice);

/// <param name="Slots">Vacío si el club no muestra precios: la IA trabaja solo con el summary.</param>
public record BotAvailabilityDto(string Date, IReadOnlyList<AvailableSlotDto> Slots, string Summary);

/// <summary>Reserva tal como la ve el bot. <c>Price</c> es null si el club no muestra precios por WhatsApp.</summary>
public record BotBookingDto(
    Guid Id, string CourtName, Models.Sport Sport, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string CustomerName, decimal? Price)
{
    public static BotBookingDto From(BookingDto b, bool showPrice) =>
        new(b.Id, b.CourtName, b.Sport, b.Date, b.StartTime, b.EndTime, b.CustomerName, showPrice ? b.Price : null);
}

public record BotBookingResultDto(BotBookingDto Booking, string Summary);

public record BotBookingsDto(IReadOnlyList<BotBookingDto> Bookings, string Summary);
