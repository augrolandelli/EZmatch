using EZmatchApi.Auth;
using EZmatchApi.Dtos;
using EZmatchApi.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EZmatchApi.Controllers;

/// <summary>
/// Configuración del club: datos y políticas, canchas, grilla de turnos y bloqueos.
/// Leer: cualquier usuario del club. Modificar: dueño (o SuperAdmin).
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Policy = Policies.ClubStaff)]
public class ClubConfigController(IClubConfigService config, ICurrentUser currentUser) : ControllerBase
{
    // ---- Club ----

    [HttpGet("club")]
    [ProducesResponseType(typeof(ClubSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct) =>
        Ok(await config.GetSettingsAsync(currentUser.ClubId, ct));

    [HttpPut("club")]
    [Authorize(Policy = Policies.ClubOwner)]
    [ProducesResponseType(typeof(ClubSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(
        UpdateClubSettingsRequest request, [FromServices] IValidator<UpdateClubSettingsRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Ok(await config.UpdateSettingsAsync(currentUser.ClubId, request, ct));
    }

    // ---- Canchas ----

    [HttpGet("courts")]
    [ProducesResponseType(typeof(IReadOnlyList<CourtDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCourts(CancellationToken ct) =>
        Ok(await config.GetCourtsAsync(currentUser.ClubId, ct));

    [HttpPost("courts")]
    [Authorize(Policy = Policies.ClubOwner)]
    [ProducesResponseType(typeof(CourtDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCourt(
        SaveCourtRequest request, [FromServices] IValidator<SaveCourtRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, await config.CreateCourtAsync(currentUser.ClubId, request, ct));
    }

    [HttpPut("courts/{id:guid}")]
    [Authorize(Policy = Policies.ClubOwner)]
    [ProducesResponseType(typeof(CourtDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCourt(
        Guid id, SaveCourtRequest request, [FromServices] IValidator<SaveCourtRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Ok(await config.UpdateCourtAsync(currentUser.ClubId, id, request, ct));
    }

    [HttpPut("courts/order")]
    [Authorize(Policy = Policies.ClubOwner)]
    [ProducesResponseType(typeof(IReadOnlyList<CourtDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReorderCourts(ReorderCourtsRequest request, CancellationToken ct) =>
        Ok(await config.ReorderCourtsAsync(currentUser.ClubId, request.CourtIds, ct));

    // ---- Grilla ----

    [HttpGet("courts/{id:guid}/slots")]
    [ProducesResponseType(typeof(IReadOnlyList<SlotTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSlots(Guid id, CancellationToken ct) =>
        Ok(await config.GetSlotsAsync(currentUser.ClubId, id, ct));

    [HttpPut("courts/{id:guid}/slots")]
    [Authorize(Policy = Policies.ClubOwner)]
    [ProducesResponseType(typeof(IReadOnlyList<SlotTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReplaceSlots(
        Guid id, ReplaceSlotsRequest request, [FromServices] IValidator<ReplaceSlotsRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Ok(await config.ReplaceSlotsAsync(currentUser.ClubId, id, request.Slots, ct));
    }

    [HttpPost("courts/{id:guid}/slots/generate")]
    [Authorize(Policy = Policies.ClubOwner)]
    [ProducesResponseType(typeof(IReadOnlyList<SlotTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateSlots(
        Guid id, GenerateSlotsRequest request, [FromServices] IValidator<GenerateSlotsRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Ok(await config.GenerateSlotsAsync(currentUser.ClubId, id, request, ct));
    }

    [HttpPost("courts/{id:guid}/slots/copy")]
    [Authorize(Policy = Policies.ClubOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CopySlots(Guid id, CopySlotsRequest request, CancellationToken ct)
    {
        await config.CopySlotsAsync(currentUser.ClubId, id, request.CourtIds, ct);
        return NoContent();
    }

    // ---- Bloqueos ----

    /// <summary>Bloqueos entre dos fechas locales (por defecto: hoy y los próximos 60 días).</summary>
    [HttpGet("blocks")]
    [ProducesResponseType(typeof(IReadOnlyList<BlockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlocks([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await config.GetBlocksAsync(currentUser.ClubId, from, to, ct));

    [HttpPost("blocks")]
    [Authorize(Policy = Policies.ClubOwner)]
    [ProducesResponseType(typeof(IReadOnlyList<BlockDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateBlock(
        CreateBlockRequest request, [FromServices] IValidator<CreateBlockRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, await config.CreateBlockAsync(currentUser.ClubId, request, ct));
    }

    [HttpDelete("blocks/{id:guid}")]
    [Authorize(Policy = Policies.ClubOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteBlock(Guid id, CancellationToken ct)
    {
        await config.DeleteBlockAsync(currentUser.ClubId, id, ct);
        return NoContent();
    }
}
