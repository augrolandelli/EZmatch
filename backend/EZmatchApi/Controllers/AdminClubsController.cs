using EZmatchApi.Auth;
using EZmatchApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Controllers;

public record ClubSummaryDto(Guid Id, string Name, string Slug, bool IsActive, int? ChatwootInboxId);

/// <summary>Gestión de clubes (solo SuperAdmin). Por ahora: listado para el selector de club del panel.</summary>
[ApiController]
[Route("api/admin/clubs")]
[Authorize(Policy = Policies.SuperAdmin)]
public class AdminClubsController(EZmatchDbContext db) : ControllerBase
{
    /// <summary>Todos los clubes, ordenados por nombre.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ClubSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await db.Clubs.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ClubSummaryDto(c.Id, c.Name, c.Slug, c.IsActive, c.ChatwootInboxId))
            .ToListAsync(ct));
}
