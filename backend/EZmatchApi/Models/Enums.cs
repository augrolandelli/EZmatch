namespace EZmatchApi.Models;

/// <summary>Deportes soportados. Se persisten como texto.</summary>
public enum Sport
{
    Padel,
    Futbol5,
    Futbol7,
    Futbol11,
    Tenis,
}

/// <summary>Ciclo de vida de una reserva. Solo <see cref="Cancelled"/> libera la cancha.</summary>
public enum BookingStatus
{
    Confirmed,
    Completed,
    Cancelled,
    NoShow,
}

/// <summary>El jugador paga en caja al terminar; el staff marca el pago desde el panel.</summary>
public enum PaymentStatus
{
    Unpaid,
    Paid,
}

public enum BookingSource
{
    WhatsApp,
    Panel,
}
