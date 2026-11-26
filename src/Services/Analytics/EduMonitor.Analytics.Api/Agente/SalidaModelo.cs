using System.Text.Json;

namespace EduMonitor.Analytics.Api.Agente;

public record SalidaChat(IReadOnlyList<string> Inferencias, IReadOnlyList<string> Recomendaciones, IReadOnlyList<string> Incertidumbre, string? Respuesta = null);

/// <summary>Lectura tolerante del JSON que devuelven los modelos pequeños (a veces añaden texto o markdown alrededor).</summary>
public static class SalidaModelo
{
    public const int MaxElementos = 4;
    public const int MaxLongitud = 420;

    /// <summary>Extrae el primer objeto JSON del texto. Devuelve null si no hay uno válido.</summary>
    public static JsonElement? ExtraerObjeto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var ini = texto.IndexOf('{');
        var fin = texto.LastIndexOf('}');
        if (ini < 0 || fin <= ini) return null;
        try
        {
            using var doc = JsonDocument.Parse(texto[ini..(fin + 1)]);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lee una propiedad como lista de textos (acepta un texto suelto). Limpia, recorta y limita.</summary>
    public static IReadOnlyList<string> Lista(JsonElement obj, string propiedad)
    {
        var encontrada = obj.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, propiedad, StringComparison.OrdinalIgnoreCase));
        if (encontrada.Value.ValueKind == JsonValueKind.Undefined) return Array.Empty<string>();

        var crudos = new List<string>();
        switch (encontrada.Value.ValueKind)
        {
            case JsonValueKind.String:
                crudos.Add(encontrada.Value.GetString() ?? "");
                break;
            case JsonValueKind.Array:
                foreach (var e in encontrada.Value.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) crudos.Add(e.GetString() ?? "");
                break;
        }
        return crudos.Select(Limpiar).Where(t => t.Length > 0).Distinct().Take(MaxElementos).ToList();
    }

    public static string Limpiar(string t)
    {
        var limpio = (t ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim().TrimStart('-', '•', '*', ' ');
        while (limpio.Contains("  ")) limpio = limpio.Replace("  ", " ");
        return limpio.Length <= MaxLongitud ? limpio : CortarEnFrase(limpio, MaxLongitud);
    }

    /// <summary>Recorta en el último punto (o, si no hay, en el último espacio) para no dejar palabras a medias.</summary>
    public static string CortarEnFrase(string texto, int max)
    {
        var tramo = texto[..max];
        var punto = tramo.LastIndexOfAny(new[] { '.', '!', '?' });
        if (punto >= max / 3) return tramo[..(punto + 1)];
        var espacio = tramo.LastIndexOf(' ');
        return (espacio > 0 ? tramo[..espacio] : tramo).TrimEnd(',', ';', ':', ' ') + "…";
    }

    public const int MaxRespuesta = 600;

    /// <summary>Respuesta conversacional del modelo (texto o lista de frases unidas). Null si no hay.</summary>
    public static string? Respuesta(JsonElement obj)
    {
        var p = obj.EnumerateObject().FirstOrDefault(x => string.Equals(x.Name, "respuesta", StringComparison.OrdinalIgnoreCase));
        if (p.Value.ValueKind == JsonValueKind.Undefined) return null;
        var partes = new List<string>();
        if (p.Value.ValueKind == JsonValueKind.String) partes.Add(p.Value.GetString() ?? "");
        else if (p.Value.ValueKind == JsonValueKind.Array)
            foreach (var e in p.Value.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) partes.Add(e.GetString() ?? "");
        var texto = string.Join(" ", partes.Select(x => x.Replace('\n', ' ').Replace('\r', ' ').Trim()).Where(x => x.Length > 0));
        while (texto.Contains("  ")) texto = texto.Replace("  ", " ");
        if (texto.Length == 0) return null;
        return texto.Length <= MaxRespuesta ? texto : CortarEnFrase(texto, MaxRespuesta);
    }

    public static SalidaChat? ParsearChat(string? texto)
    {
        var obj = ExtraerObjeto(texto);
        if (obj is null) return null;
        var inferencias = Lista(obj.Value, "inferencias");
        var recomendaciones = Lista(obj.Value, "recomendaciones");
        var incertidumbre = Lista(obj.Value, "incertidumbre");
        var respuesta = Respuesta(obj.Value);
        return inferencias.Count + recomendaciones.Count + incertidumbre.Count == 0 && respuesta is null
            ? null : new SalidaChat(inferencias, recomendaciones, incertidumbre, respuesta);
    }
}
