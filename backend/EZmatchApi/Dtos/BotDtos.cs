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

public record BotContextDto(
    Guid ClubId,
    string ClubName,
    string Today,
    string Now,
    IReadOnlyList<BotSportDto> Sports,
    string Summary);

public record BotSportDto(string Sport, string Label, IReadOnlyList<string> Courts, IReadOnlyList<int> DurationsMinutes, decimal MinPrice, decimal MaxPrice);

public record BotAvailabilityDto(string Date, IReadOnlyList<AvailableSlotDto> Slots, string Summary);

public record BotBookingResultDto(BookingDto Booking, string Summary);

public record BotBookingsDto(IReadOnlyList<BookingDto> Bookings, string Summary);
