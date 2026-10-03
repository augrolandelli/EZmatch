namespace EZmatchApi.Models;

public class Court
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClubId { get; set; }
    public Club Club { get; set; } = null!;

    public required string Name { get; set; }
    public Sport Sport { get; set; }
    public bool IsCovered { get; set; }

    /// <summary>Orden de columnas en la agenda y de asignación automática al reservar.</summary>
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public List<SlotTemplate> SlotTemplates { get; set; } = [];
}
