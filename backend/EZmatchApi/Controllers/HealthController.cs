using EZmatchApi.Data;
using Microsoft.AspNetCore.Mvc;

namespace EZmatchApi.Controllers;

/// <summary>
/// Endpoint de salud para monitoreo y verificación de despliegue.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController(EZmatchDbContext db) : ControllerBase
{
    /// <summary>
    /// Verifica que la API esté en línea y que la base de datos responda.
    /// </summary>
    /// <returns>Estado de la API y de la base, con timestamp UTC. 503 si la base no responde.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var databaseUp = await db.Database.CanConnectAsync(ct);
        var body = new
        {
            status = databaseUp ? "healthy" : "unhealthy",
            service = "EZmatchApi",
            database = databaseUp ? "up" : "down",
            timestamp = DateTime.UtcNow,
        };
        return databaseUp ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}
