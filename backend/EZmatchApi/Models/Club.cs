namespace EZmatchApi.Models;

/// <summary>Club (tenant). Todo dato de negocio cuelga de un club.</summary>
public class Club
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }

    /// <summary>Zona horaria IANA en la que se interpreta la grilla.</summary>
    public string TimeZone { get; set; } = "America/Argentina/Buenos_Aires";

    /// <summary>Inbox de Chatwoot del WhatsApp del club; así n8n resuelve a qué club pertenece un mensaje.</summary>
    public int? ChatwootAccountId { get; set; }
    public int? ChatwootInboxId { get; set; }

    // Políticas (spec §3)
    public int CancellationMinHours { get; set; } = 3;
    public int MinLeadMinutes { get; set; } = 30;
    public int BookingHorizonDays { get; set; } = 14;
    public int MaxActiveBookingsPerCustomer { get; set; } = 2;

    /// <summary>Instrucciones extra del club para el prompt del bot (ej. "se alquilan paletas").</summary>
    public string? BotInstructions { get; set; }

    /// <summary>Nombre con el que se presenta el bot (ej. "Mati"). Null = "el asistente de reservas".</summary>
    public string? AssistantName { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Court> Courts { get; set; } = [];
}
