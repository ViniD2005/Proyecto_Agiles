using System.Globalization;

namespace EduMonitor.Analytics.Api.Services;

public enum EstadoNota { Calificada = 1, Cero = 2, SinEntrega = 3, PendienteDeCalificar = 4 }
public enum NivelRiesgo { SinDatos = 0, Critico = 1, EnUmbral = 2, Aprobado = 3 }

public readonly record struct ActividadCalc(int Id, string Nombre, string Tipo, decimal Peso, int Orden);
public readonly record struct NotaCalc(int ActividadId, decimal? Nota, EstadoNota Estado);

/// <param name="NotaPonderada">Promedio ponderado (0-10) de lo ya evaluado; nulo si no hay nada evaluado.</param>
/// <param name="NotaAcumulada">Puntos ganados sobre 10 con el peso evaluado hasta ahora (RN-08).</param>
/// <param name="PesoEvaluado">Porcentaje (0-100) del parcial ya evaluado.</param>
public record ResultadoNota(decimal? NotaPonderada, decimal NotaAcumulada, decimal PesoEvaluado, int Evaluadas, int SinEntrega, int Pendientes);

public record AlertaCalc(string Tipo, string Regla, string Evidencia, string? Actividad, decimal? Valor);

/// <summary>
/// RN-09: promedios, indicadores y alertas se calculan con código determinista. El modelo de lenguaje
/// (Sprint 3) solo explica estos resultados. Esta clase no depende de base de datos ni de servicios externos.
/// </summary>
public static class CalculadoraIndicadores
{
    public const decimal CriticoPorDefecto = 5m;   // RN-04
    public const decimal AprobadoPorDefecto = 7m;  // RN-01 / RN-04
    /// <summary>Caída mínima (puntos) entre la primera y la tercera nota para considerar un descenso.</summary>
    public const decimal CaidaMinimaDescenso = 1m;

    /// <summary>Rangos del histograma del dashboard (mockup M6). El último rango incluye el 10.</summary>
    public static readonly (decimal Desde, decimal Hasta, string Etiqueta)[] Rangos =
    {
        (0m, 2m, "0–2"), (2m, 4m, "2–4"), (4m, 5m, "4–5"), (5m, 7m, "5–7"), (7m, 8.5m, "7–8,5"), (8.5m, 10m, "8,5–10")
    };

    public static decimal Redondear(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static bool EsEvaluada(EstadoNota e) => e is EstadoNota.Calificada or EstadoNota.Cero;

    public static EstadoNota ParseEstado(string? estado) => estado switch
    {
        "Calificada" => EstadoNota.Calificada,
        "Cero" => EstadoNota.Cero,
        "PendienteDeCalificar" => EstadoNota.PendienteDeCalificar,
        _ => EstadoNota.SinEntrega
    };

    public static string Fmt(decimal d) => d.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    // ------------------------------------------------------------------ nota del estudiante
    public static ResultadoNota CalcularNotaEstudiante(IReadOnlyList<ActividadCalc> actividades, IReadOnlyDictionary<int, NotaCalc> notas)
    {
        decimal suma = 0m, peso = 0m;
        int evaluadas = 0, sinEntrega = 0, pendientes = 0;
        foreach (var a in actividades)
        {
            var estado = notas.TryGetValue(a.Id, out var n) ? n.Estado : EstadoNota.SinEntrega;
            if (estado == EstadoNota.SinEntrega) sinEntrega++;
            else if (estado == EstadoNota.PendienteDeCalificar) pendientes++;

            if (!EsEvaluada(estado) || n.Nota is null || a.Peso <= 0m) continue;
            suma += n.Nota.Value * a.Peso;
            peso += a.Peso;
            evaluadas++;
        }
        var ponderada = peso > 0m ? Redondear(suma / peso) : (decimal?)null;
        return new ResultadoNota(ponderada, Redondear(suma / 100m), peso, evaluadas, sinEntrega, pendientes);
    }

    // ------------------------------------------------------------------ riesgo
    /// <summary>RN-04: Crítico &lt; crítico · En umbral entre ambos · Aprobado ≥ aprobado. Se compara la nota redondeada que ve el docente.</summary>
    public static NivelRiesgo NivelDe(decimal? nota, decimal critico, decimal aprobado)
    {
        if (nota is null) return NivelRiesgo.SinDatos;
        var n = Redondear(nota.Value);
        if (n < critico) return NivelRiesgo.Critico;
        if (n >= aprobado) return NivelRiesgo.Aprobado;
        return NivelRiesgo.EnUmbral;
    }

    // ------------------------------------------------------------------ estadística del curso
    public static decimal? Promedio(IReadOnlyCollection<decimal> valores) =>
        valores.Count == 0 ? null : Redondear(valores.Average());

    /// <summary>Desviación estándar muestral (n − 1). Con un solo dato es 0.</summary>
    public static decimal? DesviacionMuestral(IReadOnlyCollection<decimal> valores)
    {
        if (valores.Count == 0) return null;
        if (valores.Count == 1) return 0m;
        var media = (double)valores.Average();
        var suma = valores.Sum(v => Math.Pow((double)v - media, 2));
        return Redondear((decimal)Math.Sqrt(suma / (valores.Count - 1)));
    }

    public static IReadOnlyList<(string Rango, int Cantidad)> Distribucion(IEnumerable<decimal> notas)
    {
        var cuentas = new int[Rangos.Length];
        foreach (var nota in notas)
        {
            for (var i = 0; i < Rangos.Length; i++)
            {
                var (desde, hasta, _) = Rangos[i];
                var ultimo = i == Rangos.Length - 1;
                if (nota >= desde && (nota < hasta || (ultimo && nota <= hasta))) { cuentas[i]++; break; }
            }
        }
        return Rangos.Select((r, i) => (r.Etiqueta, cuentas[i])).ToList();
    }

    // ------------------------------------------------------------------ alertas con evidencia (RF-07)
    public static IReadOnlyList<AlertaCalc> GenerarAlertas(
        IReadOnlyList<ActividadCalc> actividades, IReadOnlyDictionary<int, NotaCalc> notas, decimal critico)
    {
        var alertas = new List<AlertaCalc>();
        var ordenadas = actividades.OrderBy(a => a.Orden).ToList();

        // 1) Nota baja: una actividad evaluada por debajo del umbral crítico
        foreach (var a in ordenadas)
        {
            if (!notas.TryGetValue(a.Id, out var n) || !EsEvaluada(n.Estado) || n.Nota is null) continue;
            if (n.Nota.Value < critico)
                alertas.Add(new AlertaCalc("NotaBaja",
                    $"Nota menor al umbral crítico (< {Fmt(critico)})",
                    $"Nota {Fmt(n.Nota.Value)} en {a.Nombre} (umbral crítico < {Fmt(critico)})",
                    a.Nombre, n.Nota.Value));
        }

        // 2) Descenso: las últimas 3 notas evaluadas bajan de forma consecutiva
        var evaluadas = ordenadas
            .Where(a => notas.TryGetValue(a.Id, out var n) && EsEvaluada(n.Estado) && n.Nota is not null)
            .Select(a => (Act: a, Nota: notas[a.Id].Nota!.Value))
            .ToList();
        if (evaluadas.Count >= 3)
        {
            var ult = evaluadas.Skip(evaluadas.Count - 3).ToList();
            if (ult[0].Nota > ult[1].Nota && ult[1].Nota > ult[2].Nota && ult[0].Nota - ult[2].Nota >= CaidaMinimaDescenso)
                alertas.Add(new AlertaCalc("Descenso",
                    $"Tres calificaciones consecutivas en descenso (caída de al menos {Fmt(CaidaMinimaDescenso)} punto)",
                    "Últimas notas: " + string.Join(" → ", ult.Select(x => $"{x.Act.Nombre} {Fmt(x.Nota)}")),
                    ult[2].Act.Nombre, ult[2].Nota));
        }

        // 3) Sin entrega: se distingue de la nota cero (RN-08)
        var sinEntrega = ordenadas
            .Where(a => a.Peso > 0m && (!notas.TryGetValue(a.Id, out var n) || n.Estado == EstadoNota.SinEntrega))
            .ToList();
        if (sinEntrega.Count > 0)
            alertas.Add(new AlertaCalc("SinEntrega",
                "Actividades sin entrega",
                $"{sinEntrega.Count} actividad(es) sin entrega: {string.Join(", ", sinEntrega.Select(a => a.Nombre))}",
                sinEntrega.Count == 1 ? sinEntrega[0].Nombre : null, sinEntrega.Count));

        return alertas;
    }

    // ------------------------------------------------------------------ desempeño por tema / contenido (HU-12)
    /// <summary>
    /// Promedio ponderado por el peso de las actividades evaluadas que miden el grupo (tema o contenido).
    /// Nulo si ninguna actividad mapeada fue evaluada: el mapa de calor lo muestra como «sin datos».
    /// </summary>
    public static decimal? DesempenoGrupo(
        IEnumerable<int> actividadIds,
        IReadOnlyDictionary<int, ActividadCalc> actividades,
        IReadOnlyDictionary<int, NotaCalc> notas)
    {
        decimal suma = 0m, peso = 0m;
        foreach (var id in actividadIds.Distinct())
        {
            if (!actividades.TryGetValue(id, out var a) || a.Peso <= 0m) continue;
            if (!notas.TryGetValue(id, out var n) || !EsEvaluada(n.Estado) || n.Nota is null) continue;
            suma += n.Nota.Value * a.Peso;
            peso += a.Peso;
        }
        return peso > 0m ? Redondear(suma / peso) : null;
    }
}
