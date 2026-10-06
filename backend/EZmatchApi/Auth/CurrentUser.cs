using EZmatchApi.Common;
using EZmatchApi.Models;

namespace EZmatchApi.Auth;

/// <summary>Políticas de autorización del panel.</summary>
public static class Policies
{
    /// <summary>Cualquier usuario del panel de un club (o SuperAdmin operando un club).</summary>
    public const string ClubStaff = "ClubStaff";

    /// <summary>Dueño del club (o SuperAdmin): configuración, usuarios.</summary>
    public const string ClubOwner = "ClubOwner";

    public const string SuperAdmin = "SuperAdmin";
}

public interface ICurrentUser
{
    Guid UserId { get; }
    UserRole Role { get; }

    /// <summary>
    /// Club sobre el que opera la request. Owner/Staff: su club (del token, no se puede cambiar).
    /// SuperAdmin: el indicado en el header <c>X-Club-Id</c>. Lanza 400 si no hay club.
    /// </summary>
    Guid ClubId { get; }
}

/// <summary>Usuario autenticado de la request, leído de los claims del JWT.</summary>
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string ClubHeader = "X-Club-Id";

    private HttpContext Context =>
        accessor.HttpContext ?? throw new InvalidOperationException("No hay HttpContext.");

    public Guid UserId =>
        Guid.TryParse(Context.User.FindFirst(EzClaims.UserId)?.Value, out var id)
            ? id
            : throw AppException.Unauthorized("Sesión inválida.");

    public UserRole Role =>
        Enum.TryParse<UserRole>(Context.User.FindFirst(EzClaims.Role)?.Value, out var role)
            ? role
            : throw AppException.Unauthorized("Sesión inválida.");

    public Guid ClubId
    {
        get
        {
            if (Role == UserRole.SuperAdmin)
            {
                return Guid.TryParse(Context.Request.Headers[ClubHeader].ToString(), out var selected)
                    ? selected
                    : throw new AppException("Elegí un club para operar.", StatusCodes.Status400BadRequest, "club_required");
            }

            return Guid.TryParse(Context.User.FindFirst(EzClaims.ClubId)?.Value, out var clubId)
                ? clubId
                : throw AppException.Forbidden("El usuario no pertenece a ningún club.");
        }
    }
}
