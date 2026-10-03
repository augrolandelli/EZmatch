namespace EZmatchApi.Models;

/// <summary>Bloqueo de una cancha en un rango (torneo, mantenimiento).</summary>
public class Block
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public required string Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
