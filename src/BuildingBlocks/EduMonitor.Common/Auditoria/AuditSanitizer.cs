using System.Text.RegularExpressions;

namespace EduMonitor.Common.Auditoria;

/// <summary>
/// HU-17, criterio 2: la bitácora nunca expone tokens ni contraseñas. Se aplica al escribir (MS-A y clientes)
/// y al leer, como defensa en profundidad.
/// </summary>
public static class AuditSanitizer
{
    public const string Oculto = "[OCULTO]";

    private static readonly Regex Jwt = new(@"eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*", RegexOptions.Compiled);

    private static readonly Regex Esquema = new(@"\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // password=abc · "token": "abc" · Password=xyz; · apikey: abc · Authorization: abc
    private static readonly Regex Secreto = new(
        @"(?<k>\b(?:password|passwd|pwd|contrase[nñ]a|clave|token|secret|secreto|api[-_]?key|authorization|x-goog-api-key)\b[""']?)\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s;,&]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? Limpiar(string? texto, int maxLongitud = 1000)
    {
        if (texto is null) return null;
        var limpio = Jwt.Replace(texto, Oculto);
        limpio = Esquema.Replace(limpio, m => $"{m.Groups[1].Value} {Oculto}");
        limpio = Secreto.Replace(limpio, m => $"{m.Groups["k"].Value}={Oculto}");
        return limpio.Length <= maxLongitud ? limpio : limpio[..maxLongitud];
    }

    /// <summary>Oculta la parte local de un correo para la bitácora: j***@dominio.</summary>
    public static string EnmascararCorreo(string? correo)
    {
        if (string.IsNullOrWhiteSpace(correo) || !correo.Contains('@')) return "(sin correo)";
        var partes = correo.Trim().Split('@', 2);
        var local = partes[0];
        return (local.Length == 0 ? "*" : local[..1]) + "***@" + partes[1];
    }
}
