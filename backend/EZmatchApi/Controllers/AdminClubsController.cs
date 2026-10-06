using EZmatchApi.Auth;
using EZmatchApi.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EZmatchApi.Controllers;

/// <summary>Administración de clubes (solo SuperAdmin): alta con dueño, vínculo con Chatwoot y limpieza de prueba.</summary>
[ApiController]
[Route("api/admin/clubs")]
[Authorize(Policy = Policies.SuperAdmin)]
public class AdminClubsController(IAdminClubService clubs) : ControllerBase
{
    /// <summary>Todos los clubes, ordenados por nombre (también alimenta el selector de club del panel).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminClubDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await clubs.ListAsync(ct));

    [HttpPost]
    [ProducesResponseType(typeof(AdminClubDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateClubRequest request, [FromServices] IValidator<CreateClubRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, await clubs.CreateAsync(request, ct));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AdminClubDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id, UpdateClubRequest request, [FromServices] IValidator<UpdateClubRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Ok(await clubs.UpdateAsync(id, request, ct));
    }

    /// <summary>Borra reservas, clientes y bloqueos del club. Deja canchas, horarios, configuración y usuarios.</summary>
    [HttpPost("{id:guid}/clear-activity")]
    [ProducesResponseType(typeof(ClearActivityResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearActivity(Guid id, ClearActivityRequest request, CancellationToken ct) =>
        Ok(await clubs.ClearActivityAsync(id, request.ConfirmName, ct));
}
