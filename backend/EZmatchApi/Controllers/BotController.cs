using EZmatchApi.Auth;
using EZmatchApi.Dtos;
using EZmatchApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EZmatchApi.Controllers;

/// <summary>
/// Herramientas del agente de WhatsApp (n8n). Auth: header <c>X-Bot-Key</c>.
/// El club se identifica por el inbox de Chatwoot y el jugador por su teléfono; ambos los inyecta n8n.
/// </summary>
[ApiController]
[Route("api/bot")]
[ServiceFilter<BotKeyAuthFilter>]
[EnableRateLimiting("bot")]
public class BotController(IBotService botService) : ControllerBase
{
    /// <summary>Datos del club para el system prompt (fecha actual, canchas, precios, políticas).</summary>
    [HttpGet("context")]
    [ProducesResponseType(typeof(BotContextDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContext([FromQuery] int inboxId, CancellationToken ct) =>
        Ok(await botService.GetContextAsync(inboxId, ct));

    /// <summary>Turnos libres de una fecha. <c>from</c>/<c>to</c> filtran por hora de inicio (ej. 19:00 para "a la noche").</summary>
    [HttpGet("availability")]
    [ProducesResponseType(typeof(BotAvailabilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAvailability(
        [FromQuery] int inboxId, [FromQuery] string? date, [FromQuery] string? sport,
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct) =>
        Ok(await botService.GetAvailabilityAsync(inboxId, date, sport, from, to, ct));

    /// <summary>Crea una reserva. 409 con alternativas si el turno se ocupó.</summary>
    [HttpPost("bookings")]
    [ProducesResponseType(typeof(BotBookingResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateBooking(BotCreateBookingRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await botService.CreateBookingAsync(request, ct));

    /// <summary>Reservas activas del teléfono de la conversación.</summary>
    [HttpGet("bookings")]
    [ProducesResponseType(typeof(BotBookingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBookings([FromQuery] int inboxId, [FromQuery] string phone, CancellationToken ct) =>
        Ok(await botService.GetBookingsAsync(inboxId, phone, ct));

    /// <summary>Cancela una reserva del mismo teléfono, respetando la anticipación mínima del club.</summary>
    [HttpPost("bookings/{id:guid}/cancel")]
    [ProducesResponseType(typeof(BotBookingResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelBooking(Guid id, BotCancelBookingRequest request, CancellationToken ct) =>
        Ok(await botService.CancelBookingAsync(id, request, ct));
}
