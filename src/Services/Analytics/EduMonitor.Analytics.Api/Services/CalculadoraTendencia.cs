namespace EduMonitor.Analytics.Api.Services;

/// <param name="Linea">Valores ajustados de la recta para x = 1..n y el siguiente punto (n + 1); sirve para dibujar la tendencia.</param>
public record ResultadoTendencia(
    bool Disponible, string Mensaje, int Puntos, decimal? Pendiente, decimal? R2, string Direccion,
    decimal? Proyeccion, bool AlertaTemprana, string? MensajeAlerta, IReadOnlyList<decimal> Linea);

/// <summary>
/// HU-19 (opcional): regresión lineal simple sobre las notas evaluadas, en orden de actividad.
/// Código determinista (RN-09). La proyección es una estimación de la siguiente nota, nunca un resultado definitivo.
/// </summary>
public static class CalculadoraTendencia
{
    public const int MinimoPuntos = 3;
    /// <summary>Pendiente (puntos por actividad) por debajo de la cual la tendencia se considera estable.</summary>
    public const decimal PendienteEstable = 0.25m;

    public const string Aviso = "La proyección es una estimación estadística (regresión lineal simple), no un resultado definitivo.";

    public static ResultadoTendencia Calcular(IReadOnlyList<decimal> notas, decimal critico, decimal aprobado)
    {
        var n = notas.Count;
        if (n < MinimoPuntos)
            return new ResultadoTendencia(false,
                $"Datos insuficientes: se necesitan al menos {MinimoPuntos} notas evaluadas (hay {n}).",
                n, null, null, "SinDatos", null, false, null, Array.Empty<decimal>());

        double sx = 0, sy = 0, sxy = 0, sxx = 0;
        for (var i = 0; i < n; i++)
        {
            double x = i + 1, y = (double)notas[i];
            sx += x; sy += y; sxy += x * y; sxx += x * x;
        }
        var denom = n * sxx - sx * sx;
        var pendiente = denom == 0 ? 0 : (n * sxy - sx * sy) / denom;
        var intercepto = (sy - pendiente * sx) / n;

        double ssTot = 0, ssRes = 0;
        var media = sy / n;
        for (var i = 0; i < n; i++)
        {
            var y = (double)notas[i];
            var ajustado = intercepto + pendiente * (i + 1);
            ssTot += Math.Pow(y - media, 2);
            ssRes += Math.Pow(y - ajustado, 2);
        }
        var r2 = ssTot == 0 ? 1.0 : Math.Max(0, 1 - ssRes / ssTot);

        decimal Limitar(double v) => CalculadoraIndicadores.Redondear((decimal)Math.Clamp(v, 0, 10));
        var linea = Enumerable.Range(1, n + 1).Select(x => Limitar(intercepto + pendiente * x)).ToList();
        var proyeccion = linea[^1];
        var pend = CalculadoraIndicadores.Redondear((decimal)pendiente);
        var direccion = pend > PendienteEstable ? "Ascendente" : pend < -PendienteEstable ? "Descendente" : "Estable";

        // Alerta temprana: la estimación cae bajo el umbral crítico, o la tendencia baja y la estimación queda bajo el mínimo de aprobación
        var alerta = proyeccion < critico || (direccion == "Descendente" && proyeccion < aprobado);
        var mensajeAlerta = alerta
            ? $"Alerta temprana: si la tendencia continúa, la próxima nota estimada sería {CalculadoraIndicadores.Fmt(proyeccion)} " +
              $"(mínimo de aprobación {CalculadoraIndicadores.Fmt(aprobado)}). Es una estimación, no un resultado."
            : null;

        return new ResultadoTendencia(true,
            $"Tendencia {direccion.ToLowerInvariant()} sobre {n} notas. Próxima nota estimada: {CalculadoraIndicadores.Fmt(proyeccion)}.",
            n, pend, CalculadoraIndicadores.Redondear((decimal)r2), direccion, proyeccion, alerta, mensajeAlerta, linea);
    }
}
