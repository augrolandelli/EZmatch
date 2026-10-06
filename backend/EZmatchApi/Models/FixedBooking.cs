namespace EZmatchApi.Models;

/// <summary>
/// Turno fijo: "todos los martes a las 20:00 en la Cancha 2 para Juan". No ocupa la cancha por sí mismo:
/// se materializa en <see cref="Booking"/> reales con anticipación (ver FixedBookingService), así la
/// disponibilidad, la agenda y el constraint de exclusión siguen funcionando igual.
/// </summary>
public class FixedBooking
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClubId { get; set; }
    public Guid CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>Día de la semana y hora de inicio en hora local del club (debe existir en la grilla).</summary>
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }

    /// <summary>Primera fecha (inclusive) y última (inclusive, null = sin fin).</summary>
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
}
