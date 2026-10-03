namespace EZmatchApi.Models;

/// <summary>
/// Reserva de una cancha. La no superposición por cancha la garantiza un constraint
/// de exclusión en Postgres (spec §5.3), no solo el servicio.
/// </summary>
public class Booking
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClubId { get; set; }
    public Guid CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>Instantes en UTC.</summary>
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    /// <summary>Copiado de la grilla al reservar: cambios de precio no afectan reservas existentes.</summary>
    public decimal Price { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public BookingSource Source { get; set; }

    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
