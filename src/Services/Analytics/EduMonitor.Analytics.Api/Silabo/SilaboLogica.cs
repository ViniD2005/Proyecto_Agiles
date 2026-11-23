using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EduMonitor.Analytics.Api.Agente;
using EduMonitor.Analytics.Api.Agente.Llm;
using EduMonitor.Analytics.Api.Contracts;

namespace EduMonitor.Analytics.Api.Silabo;

/// <summary>Límites alineados con MS-B (hasta 10 unidades, nombres de 200 caracteres) y con un tamaño razonable de pantalla.</summary>
public static class LimitesSilabo
{
    public const int MaxUnidades = 10, MaxTemasPorUnidad = 12, MaxContenidosPorTema = 8, MaxNombre = 200;
    public const int MaxCaracteresTexto = 14000;
}

public static class TextoSilabo
{
    /// <summary>Quita caracteres de control, compacta espacios y limita el tamaño que se envía al modelo.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
            sb.Append(c == '\r' ? '\n' : c == '\t' ? ' ' : char.IsControl(c) && c != '\n' ? ' ' : c);
        var lineas = sb.ToString().Split('\n')
            .Select(l => Regex.Replace(l, @"[  ]{2,}", " ").Trim())
            .Where(l => l.Length > 0);
        var limpio = string.Join("\n", lineas);
        return limpio.Length <= LimitesSilabo.MaxCaracteresTexto ? limpio : limpio[..LimitesSilabo.MaxCaracteresTexto];
    }
}

/// <summary>Convierte y depura la propuesta (del modelo o de los patrones) a lo que acepta la importación de MS-B.</summary>
public static class PropuestaSilaboParser
{
    public static IReadOnlyList<SilaboUnidadDto> Depurar(IEnumerable<SilaboUnidadDto> unidades)
    {
        string Nombre(string? n)
        {
            var t = Regex.Replace((n ?? string.Empty).Trim(), @"\s+", " ").Trim('-', '•', '*', '·', ' ', ':', ';', '.');
            return t.Length <= LimitesSilabo.MaxNombre ? t : t[..LimitesSilabo.MaxNombre].TrimEnd();
        }
        List<string> Unicos(IEnumerable<string> nombres)
        {
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return nombres.Select(Nombre).Where(n => n.Length > 0 && vistos.Add(n)).ToList();
        }

        var resultado = new List<SilaboUnidadDto>();
        var unidadesVistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in unidades)
        {
            var nombreU = Nombre(u.Nombre);
            if (nombreU.Length == 0 || !unidadesVistas.Add(nombreU)) continue;

            var temas = new List<SilaboTemaDto>();
            var temasVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in u.Temas ?? new List<SilaboTemaDto>())
            {
                var nombreT = Nombre(t.Nombre);
                if (nombreT.Length == 0 || !temasVistos.Add(nombreT)) continue;
                temas.Add(new SilaboTemaDto(nombreT, Unicos(t.Contenidos ?? new List<string>()).Take(LimitesSilabo.MaxContenidosPorTema).ToList()));
                if (temas.Count >= LimitesSilabo.MaxTemasPorUnidad) break;
            }
            resultado.Add(new SilaboUnidadDto(nombreU, temas));
            if (resultado.Count >= LimitesSilabo.MaxUnidades) break;
        }
        return resultado;
    }

    /// <summary>Lee el JSON {"unidades":[{"nombre","temas":[{"nombre","contenidos":[]}]}]} del modelo. Null si no hay nada utilizable.</summary>
    public static IReadOnlyList<SilaboUnidadDto>? ParsearModelo(string? texto)
    {
        var obj = SalidaModelo.ExtraerObjeto(texto);
        if (obj is null) return null;
        var prop = obj.Value.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, "unidades", StringComparison.OrdinalIgnoreCase));
        if (prop.Value.ValueKind != JsonValueKind.Array) return null;

        string? Texto(JsonElement e, string nombre)
        {
            var p = e.ValueKind == JsonValueKind.Object
                ? e.EnumerateObject().FirstOrDefault(x => string.Equals(x.Name, nombre, StringComparison.OrdinalIgnoreCase))
                : default;
            return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
        }

        var unidades = new List<SilaboUnidadDto>();
        foreach (var u in prop.Value.EnumerateArray())
        {
            var temas = new List<SilaboTemaDto>();
            var tp = u.ValueKind == JsonValueKind.Object
                ? u.EnumerateObject().FirstOrDefault(x => string.Equals(x.Name, "temas", StringComparison.OrdinalIgnoreCase)) : default;
            if (tp.Value.ValueKind == JsonValueKind.Array)
                foreach (var t in tp.Value.EnumerateArray())
                {
                    var contenidos = new List<string>();
                    var cp = t.ValueKind == JsonValueKind.Object
                        ? t.EnumerateObject().FirstOrDefault(x => string.Equals(x.Name, "contenidos", StringComparison.OrdinalIgnoreCase)) : default;
                    if (cp.Value.ValueKind == JsonValueKind.Array)
                        contenidos.AddRange(cp.Value.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString() ?? ""));
                    temas.Add(new SilaboTemaDto(Texto(t, "nombre") ?? string.Empty, contenidos));
                }
            unidades.Add(new SilaboUnidadDto(Texto(u, "nombre") ?? string.Empty, temas));
        }
        var depuradas = Depurar(unidades);
        return depuradas.Count == 0 ? null : depuradas;
    }

    public const string InstruccionesSistema =
        "Eres un asistente académico. Del texto de un sílabo universitario extrae la estructura temática en español.\n" +
        "Reglas obligatorias:\n" +
        "1. Usa SOLO lo que aparece en el texto; no inventes unidades, temas ni contenidos.\n" +
        "2. Mantén los nombres tal como figuran en el sílabo (puedes acortarlos si son muy largos).\n" +
        "3. Máximo 10 unidades, 12 temas por unidad y 8 contenidos mínimos por tema.\n" +
        "4. Si un tema no tiene contenidos claros, devuelve la lista de contenidos vacía.\n" +
        "Devuelve SOLO un objeto JSON con esta forma exacta:\n" +
        "{\"unidades\":[{\"nombre\":\"...\",\"temas\":[{\"nombre\":\"...\",\"contenidos\":[\"...\"]}]}]}";

    public static IReadOnlyList<LlmMensaje> Mensajes(string texto) => new[]
    {
        new LlmMensaje(LlmMensaje.Sistema, InstruccionesSistema),
        new LlmMensaje(LlmMensaje.Usuario, "TEXTO DEL SÍLABO:\n" + texto)
    };
}

/// <summary>
/// Respaldo sin modelo: reconoce encabezados de unidad («UNIDAD 1: …»), temas numerados (1.1) y viñetas.
/// Es una heurística: sirve para sílabos con formato habitual; el docente siempre revisa y corrige antes de guardar.
/// </summary>
public static class ExtractorSilaboPorPatrones
{
    private static readonly Regex Unidad = new(
        @"^(?:unidad|capitulo|capítulo|módulo|modulo)\s*(?:n[°ºo.]?\s*)?(\d{1,2}|[ivx]{1,4})\s*[:.\-–—)]*\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TemaNumerado = new(@"^(\d{1,2})\.(\d{1,2})[.)]?\s+(\S.*)$", RegexOptions.Compiled);
    private static readonly Regex ContenidoNumerado = new(@"^(\d{1,2})\.(\d{1,2})\.(\d{1,2})[.)]?\s+(\S.*)$", RegexOptions.Compiled);
    private static readonly Regex Vineta = new(@"^[•\-–*·▪●○]\s*(\S.*)$", RegexOptions.Compiled);

    public static IReadOnlyList<SilaboUnidadDto> Extraer(string texto)
    {
        var unidades = new List<(string Nombre, List<(string Nombre, List<string> Contenidos)> Temas)>();

        var temasNumerados = false;   // si la unidad numera sus temas (1.1, 1.2…), las viñetas son contenidos; si no, cada viñeta es un tema
        foreach (var cruda in TextoSilabo.Normalizar(texto).Split('\n'))
        {
            var linea = cruda.Trim();
            if (linea.Length < 3) continue;

            var mu = Unidad.Match(linea);
            if (mu.Success)
            {
                var numero = mu.Groups[1].Value.ToUpperInvariant();
                var resto = mu.Groups[2].Value.Trim();
                unidades.Add((resto.Length > 0 ? $"Unidad {numero}: {resto}" : $"Unidad {numero}", new()));
                temasNumerados = false;
                continue;
            }
            if (unidades.Count == 0) continue;
            var actual = unidades[^1];

            var mc = ContenidoNumerado.Match(linea);
            if (mc.Success)
            {
                if (actual.Temas.Count > 0) actual.Temas[^1].Contenidos.Add(mc.Groups[4].Value);
                continue;
            }
            var mt = TemaNumerado.Match(linea);
            if (mt.Success) { actual.Temas.Add((mt.Groups[3].Value, new List<string>())); temasNumerados = true; continue; }

            var mv = Vineta.Match(linea);
            if (mv.Success)
            {
                // viñeta bajo un tema numerado → contenido; viñetas directamente bajo la unidad → un tema cada una
                if (temasNumerados && actual.Temas.Count > 0) actual.Temas[^1].Contenidos.Add(mv.Groups[1].Value);
                else actual.Temas.Add((mv.Groups[1].Value, new List<string>()));
            }
        }

        return PropuestaSilaboParser.Depurar(unidades.Select(u =>
            new SilaboUnidadDto(u.Nombre, u.Temas.Select(t => new SilaboTemaDto(t.Nombre, t.Contenidos)).ToList())));
    }
}
