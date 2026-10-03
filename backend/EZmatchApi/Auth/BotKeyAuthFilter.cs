using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace EZmatchApi.Auth;

public class BotSettings
{
    public const string SectionName = "Bot";

    /// <summary>Clave compartida con n8n (header <c>X-Bot-Key</c>). Mínimo 32 caracteres.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Autoriza los endpoints <c>/api/bot</c> comparando el header <c>X-Bot-Key</c> en tiempo constante.
/// </summary>
public class BotKeyAuthFilter(IOptions<BotSettings> settings) : IAuthorizationFilter
{
    public const string HeaderName = "X-Bot-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = Encoding.UTF8.GetBytes(settings.Value.ApiKey);
        var provided = Encoding.UTF8.GetBytes(context.HttpContext.Request.Headers[HeaderName].ToString());

        if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Clave de bot inválida.", code = "unauthorized" });
        }
    }
}
