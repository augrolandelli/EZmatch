namespace EZmatchApi.Common;

/// <summary>
/// Excepción de aplicación con código HTTP y mensaje seguro para el cliente.
/// Los detalles internos nunca se exponen (ver spec §10).
/// </summary>
public class AppException(string message, int statusCode = StatusCodes.Status400BadRequest, string code = "app_error")
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;

    /// <summary>Datos extra para el cliente (ej. turnos alternativos ante un conflicto).</summary>
    public object? Details { get; init; }

    public static AppException NotFound(string message) =>
        new(message, StatusCodes.Status404NotFound, "not_found");

    public static AppException Conflict(string message, object? details = null) =>
        new(message, StatusCodes.Status409Conflict, "conflict") { Details = details };

    public static AppException Unauthorized(string message) =>
        new(message, StatusCodes.Status401Unauthorized, "unauthorized");

    public static AppException Forbidden(string message, string code = "forbidden") =>
        new(message, StatusCodes.Status403Forbidden, code);
}
