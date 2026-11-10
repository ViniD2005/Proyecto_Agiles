using System.Globalization;
using System.Text.RegularExpressions;
using EduMonitor.Academic.Api.Domain;

namespace EduMonitor.Academic.Api.Services;

/// <summary>
/// Reglas deterministas para interpretar lo que devuelve Moodle (RN-08, RN-09).
/// Sin acceso a base de datos ni a Moodle: se prueba con casos calculados a mano.
/// </summary>
public static class GradeStateClassifier
{
    /// <summary>
    /// RN-08: nota cero ≠ sin entrega ≠ pendiente de calificar.
    /// </summary>
    public static EstadoCalificacion Clasificar(decimal? nota, bool entregado)
    {
        if (nota.HasValue) return nota.Value == 0m ? EstadoCalificacion.Cero : EstadoCalificacion.Calificada;
        return entregado ? EstadoCalificacion.PendienteDeCalificar : EstadoCalificacion.SinEntrega;
    }

    /// <summary>Lleva la nota a la escala 0-10 (RN-01). Si Moodle usa otra escala máxima se proporciona.</summary>
    public static decimal? NormalizarEscala(decimal? bruta, decimal? maxima)
    {
        if (!bruta.HasValue) return null;
        var valor = (maxima.HasValue && maxima.Value > 0m && maxima.Value != 10m)
            ? bruta.Value / maxima.Value * 10m
            : bruta.Value;
        valor = Math.Clamp(valor, 0m, 10m);
        return Math.Round(valor, 2, MidpointRounding.AwayFromZero);
    }

    private static readonly Regex Numerico = new(@"^\d+([.,]\d+)?$", RegexOptions.Compiled);

    /// <summary>Respaldo cuando «graderaw» llega vacío pero «gradeformatted» trae el número («8,50»). El guion «-» significa sin nota.</summary>
    public static decimal? ParseNotaFormateada(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        if (!Numerico.IsMatch(t)) return null;
        return decimal.Parse(t.Replace(',', '.'), CultureInfo.InvariantCulture);
    }

    public static string NombreVisible(string? nombres, string? apellidos, string? correo)
    {
        var completo = $"{nombres} {apellidos}".Trim();
        if (completo.Length > 0) return completo;
        if (!string.IsNullOrWhiteSpace(correo)) return correo.Split('@')[0];
        return "Estudiante";
    }
}
