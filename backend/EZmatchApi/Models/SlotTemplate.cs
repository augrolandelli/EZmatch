namespace EZmatchApi.Models;

/// <summary>
/// Un turno de la grilla semanal de una cancha, en hora local del club.
/// El turno pertenece al día en que empieza aunque termine pasada la medianoche.
/// </summary>
public class SlotTemplate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
}
