using System.Globalization;
using System.Text;
using EduMonitor.Academic.Api.Domain;

namespace EduMonitor.Academic.Api.Services;

/// <summary>
/// Clasifica las actividades de Moodle en Evaluación / APE / Trabajo autónomo (T6.2)
/// y calcula su peso dentro del parcial según RN-02 (40 / 30 / 30).
/// Sprint 1: por nombre de la actividad. Sprint 2: se contrasta con las categorías del libro de calificaciones.
/// </summary>
public static class ActivityClassifier
{
    public const decimal PesoEvaluaciones = 40m;
    public const decimal PesoApe = 30m;
    public const decimal PesoAutonomo = 30m;

    /// <summary>Solo estos módulos generan calificación en el parcial.</summary>
    public static readonly HashSet<string> ModulosCalificables = new(StringComparer.OrdinalIgnoreCase) { "assign", "quiz" };

    public static TipoActividad Clasificar(string nombre)
    {
        var n = Normalizar(nombre);
        var palabras = n.Split(new[] { ' ', '-', '_', '.', ':', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);

        if (palabras.Contains("ape") || n.Contains("practico experimental") || n.Contains("practica experimental"))
            return TipoActividad.APE;

        if (n.Contains("trabajo autonomo") || palabras.Contains("autonomo") || palabras.Contains("aa") ||
            palabras.Contains("ta") || palabras.Any(p => p.Length >= 2 && p[0] == 't' && p[1..].All(char.IsDigit)))
            return TipoActividad.TrabajoAutonomo;

        if (n.Contains("evaluacion") || palabras.Contains("examen") || palabras.Contains("prueba") || palabras.Contains("acd"))
            return TipoActividad.Evaluacion;

        return TipoActividad.Otra;
    }

    /// <summary>Reparte el peso de cada categoría entre sus actividades (ej. 2 APE -> 15 % c/u).</summary>
    public static void AsignarPesos(IEnumerable<Actividad> actividades)
    {
        var lista = actividades.ToList();
        Repartir(lista, TipoActividad.Evaluacion, PesoEvaluaciones);
        Repartir(lista, TipoActividad.APE, PesoApe);
        Repartir(lista, TipoActividad.TrabajoAutonomo, PesoAutonomo);
        foreach (var a in lista.Where(a => a.Tipo == TipoActividad.Otra)) a.Peso = 0;
    }

    private static void Repartir(List<Actividad> lista, TipoActividad tipo, decimal pesoTotal)
    {
        var grupo = lista.Where(a => a.Tipo == tipo).ToList();
        if (grupo.Count == 0) return;
        var peso = Math.Round(pesoTotal / grupo.Count, 2);
        foreach (var a in grupo) a.Peso = peso;
    }

    public static string Normalizar(string texto)
    {
        var formD = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
