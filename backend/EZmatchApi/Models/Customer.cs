namespace EZmatchApi.Models;

/// <summary>Jugador de un club, identificado por teléfono (E.164). Se crea en su primera reserva.</summary>
public class Customer
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClubId { get; set; }
    public Club Club { get; set; } = null!;

    public required string Phone { get; set; }
    public required string Name { get; set; }

    /// <summary>Bloqueado por el club (ej. ausencias reiteradas): el bot no le permite reservar.</summary>
    public bool IsBlocked { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
