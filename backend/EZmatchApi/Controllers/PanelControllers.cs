using EZmatchApi.Auth;
using EZmatchApi.Dtos;
using EZmatchApi.Models;
using EZmatchApi.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EZmatchApi.Controllers;

/// <summary>Agenda del día del club (panel). El club sale del token (o de X-Club-Id para el SuperAdmin).</summary>
[ApiController]
[Route("api/agenda")]
[Authorize(Policy = Policies.ClubStaff)]
public class AgendaController(IAgendaService agenda, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Canchas con sus turnos libres, reservas y bloqueos de una fecha (hoy si no se indica).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AgendaDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] DateOnly? date, CancellationToken ct) =>
        Ok(await agenda.GetDayAsync(currentUser.ClubId, date, ct));
}

/// <summary>Reservas desde el mostrador: alta manual, cobro, ausencia y cancelación.</summary>
[ApiController]
[Route("api/bookings")]
[Authorize(Policy = Policies.ClubStaff)]
public class BookingsController(IPanelBookingService panel, IBookingService bookings, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Reserva manual. 409 con alternativas si el turno se ocupó.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        PanelCreateBookingRequest request, [FromServices] IValidator<PanelCreateBookingRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, await panel.CreateAsync(currentUser.ClubId, request, ct));
    }

    /// <summary>Cancela una reserva (desde el panel no hay límite de anticipación).</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, PanelCancelBookingRequest request, CancellationToken ct) =>
        Ok(await bookings.CancelAsync(currentUser.ClubId, id, null, BookingSource.Panel, request.Reason, ct));

    /// <summary>Marca la reserva como pagada o pendiente.</summary>
    [HttpPut("{id:guid}/payment")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetPayment(Guid id, SetPaymentRequest request, CancellationToken ct) =>
        Ok(await panel.SetPaymentAsync(currentUser.ClubId, id, request.Paid, ct));

    /// <summary>Marca o desmarca "no vino".</summary>
    [HttpPut("{id:guid}/no-show")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetNoShow(Guid id, SetNoShowRequest request, CancellationToken ct) =>
        Ok(await panel.SetNoShowAsync(currentUser.ClubId, id, request.NoShow, ct));
}

/// <summary>Clientes del club.</summary>
[ApiController]
[Route("api/customers")]
[Authorize(Policy = Policies.ClubStaff)]
public class CustomersController(IPanelBookingService panel, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Busca por nombre o teléfono (mínimo 2 caracteres). Para autocompletar al reservar.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] string? search, CancellationToken ct) =>
        Ok(await panel.SearchCustomersAsync(currentUser.ClubId, search, ct));
}
