using System.Text.Encodings.Web;
using System.Text.Json;
using EduMonitor.Analytics.Api.Services;
using EduMonitor.Common.Results;

namespace EduMonitor.Analytics.Api.Agente.Herramientas;

public enum TipoParametro { Entero, Nivel }

public record ParametroHerramienta(string Nombre, TipoParametro Tipo, bool Obligatorio, string Descripcion);

/// <summary>Quién consulta y qué cursos puede ver. La herramienta nunca recibe más permisos que el docente.</summary>
public record ContextoHerramienta(string DocenteEmail, IReadOnlySet<int> MateriasAutorizadas);

/// <param name="Hechos">Líneas con marcadores {est:ID}: nunca contienen nombres de estudiantes.</param>
/// <param name="DatosJson">Detalle para el modelo (también con marcadores). Puede ser nulo.</param>
public record ResultadoHerramienta(bool Exito, string Resumen, IReadOnlyList<string> Hechos, string? DatosJson, string? Error, bool Denegada = false)
{
    public static ResultadoHerramienta Ok(string resumen, IReadOnlyList<string> hechos, object? datos = null) =>
        new(true, resumen, hechos, datos is null ? null : HerramientasUtil.Serializar(datos), null);

    public static ResultadoHerramienta Falla(string error, bool denegada = false) =>
        new(false, error, Array.Empty<string>(), null, error, denegada);

    public static ResultadoHerramienta DeResultado<T>(Resultado<T> r, string contexto) =>
        Falla($"No se pudo obtener {contexto}: {r.Mensaje ?? "error desconocido"}", r.Codigo == CodigoResultado.Prohibido);
}

/// <summary>HU-13: herramienta controlada del agente. Solo lee datos ya autorizados y calculados por el código (RN-09).</summary>
public interface IHerramienta
{
    string Nombre { get; }
    string Descripcion { get; }
    IReadOnlyList<ParametroHerramienta> Parametros { get; }
    Task<ResultadoHerramienta> EjecutarAsync(ContextoHerramienta contexto, IReadOnlyDictionary<string, string> argumentos, CancellationToken ct);
}

public static class HerramientasUtil
{
    public static readonly string[] Niveles = { "Critico", "EnUmbral", "Aprobado" };

    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static string Serializar(object datos) => JsonSerializer.Serialize(datos, Opciones);

    public static int Entero(IReadOnlyDictionary<string, string> args, string nombre) => int.Parse(args[nombre]);

    public static string F(decimal? v) => v is null ? "—" : CalculadoraIndicadores.Fmt(v.Value);

    /// <summary>
    /// Valida los argumentos contra la declaración de la herramienta (HU-13, criterio 2: «parámetros validados»).
    /// Solo pasan los parámetros declarados; materiaId debe ser de un curso autorizado.
    /// </summary>
    public static (IReadOnlyDictionary<string, string>? Validos, string? Error, bool Denegado) Validar(
        IHerramienta herramienta, IReadOnlyDictionary<string, string> recibidos, ContextoHerramienta ctx)
    {
        var validos = new Dictionary<string, string>();
        foreach (var p in herramienta.Parametros)
        {
            if (!recibidos.TryGetValue(p.Nombre, out var valor) || string.IsNullOrWhiteSpace(valor))
            {
                if (p.Obligatorio) return (null, $"Falta el parámetro obligatorio «{p.Nombre}».", false);
                continue;
            }
            valor = valor.Trim();
            switch (p.Tipo)
            {
                case TipoParametro.Entero:
                    if (!int.TryParse(valor, out var n) || n <= 0)
                        return (null, $"El parámetro «{p.Nombre}» debe ser un número entero positivo.", false);
                    if (p.Nombre == "materiaId" && !ctx.MateriasAutorizadas.Contains(n))
                        return (null, "La materia indicada no pertenece a los cursos autorizados del docente.", true);
                    valor = n.ToString();
                    break;
                case TipoParametro.Nivel:
                    var nivel = Niveles.FirstOrDefault(x => string.Equals(x, valor, StringComparison.OrdinalIgnoreCase));
                    if (nivel is null) return (null, $"El parámetro «{p.Nombre}» debe ser uno de: {string.Join(", ", Niveles)}.", false);
                    valor = nivel;
                    break;
            }
            validos[p.Nombre] = valor;
        }
        return (validos, null, false);
    }
}

public sealed class RegistroHerramientas(IEnumerable<IHerramienta> herramientas)
{
    private readonly Dictionary<string, IHerramienta> _porNombre = herramientas.ToDictionary(h => h.Nombre, StringComparer.Ordinal);
    public IReadOnlyCollection<IHerramienta> Todas => _porNombre.Values;
    public IHerramienta? Obtener(string nombre) => _porNombre.GetValueOrDefault(nombre);
}
