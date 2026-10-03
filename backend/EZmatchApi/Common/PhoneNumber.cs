namespace EZmatchApi.Common;

/// <summary>Normalización de teléfonos a formato E.164 (+ y solo dígitos).</summary>
public static class PhoneNumber
{
    /// <summary>
    /// Normaliza "+54 9 341 123-4567" → "+5493411234567".
    /// Se asume que el número ya trae código de país (así llega de WhatsApp/Chatwoot).
    /// </summary>
    public static string Normalize(string? raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is < 8 or > 15)
        {
            throw new AppException("El teléfono no es válido.", StatusCodes.Status400BadRequest, "invalid_phone");
        }
        return "+" + digits;
    }
}
