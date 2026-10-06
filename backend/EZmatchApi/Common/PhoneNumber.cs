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

    /// <summary>
    /// Teléfono tipeado en el panel por el club. Si trae "+", se toma tal cual (internacional).
    /// Si no, se asume un celular argentino y se lleva al formato de WhatsApp (+54 9 área número),
    /// para que el mismo cliente se reconozca reserve por el mostrador o por el bot:
    /// "341 555-0001" / "0341 555 0001" / "54 341 5550001" / "549 341 5550001" → "+5493415550001".
    /// </summary>
    public static string NormalizeArgentine(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.StartsWith('+')) return Normalize(text);

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("00")) return Normalize(digits[2..]);

        if (digits.StartsWith("54") && digits.Length >= 12)
        {
            var national = digits[2..];
            return Normalize("549" + (national.StartsWith('9') ? national[1..] : national));
        }

        // Número nacional: sin el 0 de larga distancia, código de área + número = 10 dígitos.
        if (digits.StartsWith('0')) digits = digits[1..];
        if (digits.Length != 10)
        {
            throw new AppException(
                "El teléfono no es válido. Ingresá código de área + número, sin 0 ni 15 (ej. 341 555 0001).",
                StatusCodes.Status400BadRequest, "invalid_phone");
        }
        return "+549" + digits;
    }
}
