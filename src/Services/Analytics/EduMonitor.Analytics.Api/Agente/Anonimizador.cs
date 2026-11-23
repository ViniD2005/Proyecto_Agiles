using System.Text;
using System.Text.RegularExpressions;

namespace EduMonitor.Analytics.Api.Agente;

/// <summary>Marcadores de estudiante usados dentro de los textos de las herramientas: {est:123}.</summary>
public static class Marcador
{
    public static readonly Regex Patron = new(@"\{est:(\d+)\}", RegexOptions.Compiled);
    public static string Est(int estudianteId) => $"{{est:{estudianteId}}}";
}

/// <summary>
/// RN-11 / HU-13 criterio 4: el modelo de lenguaje solo recibe códigos anónimos (E01, E02…), nunca nombres.
/// Las herramientas escriben a los estudiantes como marcadores {est:ID}; esta clase los convierte en código para el modelo
/// y en nombre real para el docente. También anonimiza la pregunta del docente y revisa lo que responde el modelo.
/// </summary>
public sealed class Anonimizador
{
    private static readonly Regex Palabra = new(@"\p{L}+", RegexOptions.Compiled);
    private static readonly Regex CodigoAlias = new(@"\bE(\d{1,4})\b", RegexOptions.Compiled);
    private static readonly HashSet<string> Excluidas = new()
    {
        "del", "los", "las", "van", "von", "san", "con", "por", "que", "una", "uno", "sus", "mis", "nos", "ese", "esa", "est"
    };

    private readonly Dictionary<int, string> _alias = new();
    private readonly Dictionary<int, int> _idPorIndice = new();
    private readonly Dictionary<int, string> _nombres = new();
    private readonly Dictionary<string, HashSet<int>> _tokens = new();

    public Anonimizador(IEnumerable<(int Id, string Nombre)> estudiantes)
    {
        var lista = estudiantes.OrderBy(e => e.Id).ToList();
        var ancho = Math.Max(2, lista.Count.ToString().Length);
        var indice = 0;
        foreach (var (id, nombre) in lista)
        {
            indice++;
            _alias[id] = "E" + indice.ToString().PadLeft(ancho, '0');
            _idPorIndice[indice] = id;
            _nombres[id] = string.IsNullOrWhiteSpace(nombre) ? $"Estudiante {id}" : nombre.Trim();
            foreach (Match m in Palabra.Matches(nombre ?? string.Empty))
            {
                var token = Normalizar(m.Value);
                if (token.Length < 3 || Excluidas.Contains(token)) continue;
                if (!_tokens.TryGetValue(token, out var set)) _tokens[token] = set = new HashSet<int>();
                set.Add(id);
            }
        }
    }

    public int Total => _alias.Count;

    public string Alias(int estudianteId) => _alias.TryGetValue(estudianteId, out var a) ? a : "[estudiante]";

    public string Nombre(int estudianteId) => _nombres.TryGetValue(estudianteId, out var n) ? n : $"Estudiante {estudianteId}";

    /// <summary>Texto con marcadores → texto con códigos anónimos (lo único que ve el modelo).</summary>
    public string ParaModelo(string texto) =>
        Marcador.Patron.Replace(texto, m => Alias(int.Parse(m.Groups[1].Value)));

    /// <summary>Texto con marcadores → texto con nombres reales (lo que ve el docente).</summary>
    public string ParaDocente(string texto) =>
        Marcador.Patron.Replace(texto, m => Nombre(int.Parse(m.Groups[1].Value)));

    /// <summary>
    /// Reemplaza los nombres que el docente escribió en su pregunta por el código del estudiante. Un nombre compartido
    /// por varios estudiantes se oculta como «[estudiante]». Devuelve también los estudiantes identificados sin ambigüedad.
    /// </summary>
    public (string Texto, IReadOnlyList<int> Mencionados) AnonimizarPregunta(string pregunta)
    {
        var mencionados = new List<int>();
        var texto = Palabra.Replace(pregunta, m =>
        {
            var token = Normalizar(m.Value);
            if (!_tokens.TryGetValue(token, out var ids)) return m.Value;
            if (ids.Count == 1)
            {
                var id = ids.First();
                if (!mencionados.Contains(id)) mencionados.Add(id);
                return Alias(id);
            }
            return "[estudiante]";
        });

        // «Joel Herrera» produce «E03 E03»: se deja un solo código
        texto = Regex.Replace(texto, @"\b(E\d{1,4})(?:\s+\1\b)+", "$1");
        texto = Regex.Replace(texto, @"\[estudiante\](?:\s+\[estudiante\])+", "[estudiante]");

        // Si el docente escribió directamente un código (E03), también cuenta como mención
        foreach (Match m in CodigoAlias.Matches(texto))
            if (int.TryParse(m.Groups[1].Value, out var i) && _idPorIndice.TryGetValue(i, out var id) && !mencionados.Contains(id))
                mencionados.Add(id);

        return (texto, mencionados);
    }

    /// <summary>True si el texto del modelo cita un código que no corresponde a ningún estudiante del curso (dato inventado).</summary>
    public bool TieneCodigoInventado(string textoModelo) =>
        CodigoAlias.Matches(textoModelo).Any(m =>
            !(int.TryParse(m.Groups[1].Value, out var i) && _idPorIndice.ContainsKey(i)));

    /// <summary>Códigos del modelo → nombres reales para el docente.</summary>
    public string RestaurarParaDocente(string textoModelo) =>
        CodigoAlias.Replace(textoModelo, m =>
            int.TryParse(m.Groups[1].Value, out var i) && _idPorIndice.TryGetValue(i, out var id) ? Nombre(id) : m.Value);

    /// <summary>Minúsculas y sin tildes (sin depender de la normalización Unicode del sistema).</summary>
    public static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.ToLowerInvariant())
        {
            sb.Append(c switch
            {
                'á' or 'à' or 'ä' or 'â' => 'a',
                'é' or 'è' or 'ë' or 'ê' => 'e',
                'í' or 'ì' or 'ï' or 'î' => 'i',
                'ó' or 'ò' or 'ö' or 'ô' => 'o',
                'ú' or 'ù' or 'ü' or 'û' => 'u',
                'ñ' => 'n',
                _ => c
            });
        }
        return sb.ToString();
    }
}
