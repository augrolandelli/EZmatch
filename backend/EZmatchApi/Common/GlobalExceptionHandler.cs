using Microsoft.AspNetCore.Diagnostics;

namespace EZmatchApi.Common;

/// <summary>
/// Manejador global de excepciones: formato uniforme { message, code, details? }
/// y sin stack traces en las respuestas.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is FluentValidation.ValidationException validation)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                message = validation.Errors.First().ErrorMessage,
                code = "validation_error",
                errors = validation.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
            }, cancellationToken);
            return true;
        }

        if (exception is AppException appException)
        {
            httpContext.Response.StatusCode = appException.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                message = appException.Message,
                code = appException.Code,
                details = appException.Details,
            }, cancellationToken);
            return true;
        }

        logger.LogError(exception, "Error no controlado procesando {Path}", httpContext.Request.Path);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            message = "Error interno del servidor.",
            code = "internal_error",
        }, cancellationToken);
        return true;
    }
}
