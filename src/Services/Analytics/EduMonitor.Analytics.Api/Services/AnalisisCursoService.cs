using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Common.Results;

namespace EduMonitor.Analytics.Api.Services;

public record TemaResumen(int TemaId, string Tema, string Unidad, decimal? Promedio, string Nivel, int EstudiantesCriticos, int EstudiantesConDatos);
public record ActividadProblema(string Nombre, decimal? Promedio, int Calificadas, int Ceros, int SinEntrega, int Pendientes);

/// <summary>Foto del curso con los mismos números del dashboard: la usan el agente, el reporte y las reglas de recomendación.</summary>
public record AnalisisCurso(
    int MateriaId, string Materia, DateTime? ActualizadoUtc, UmbralesDto Umbrales, IndicadorCursoDto Curso,
    IReadOnlyList<TemaResumen> Temas, IReadOnlyList<ActividadProblema> Actividades, int ActividadesSinMapear, bool TieneEstructura);

/// <summary>
/// Reúne indicadores (HU-10) y mapa de calor (HU-12) en un solo objeto. No recalcula nada: reutiliza AnaliticaService,
/// por eso las cifras del reporte (HU-16, criterio 4) coinciden con el dashboard.
/// </summary>
public class AnalisisCursoService(AnaliticaService analitica)
{
    public async Task<Resultado<AnalisisCurso>> ObtenerAsync(int materiaId, string? email, CancellationToken ct = default)
    {
        var ind = await analitica.ObtenerIndicadoresAsync(materiaId, email, ct);
        if (!ind.EsOk) return Resultado<AnalisisCurso>.Falla(ind.Codigo, ind.Mensaje ?? "No se pudo obtener los indicadores.");
        var mapa = await analitica.ObtenerMapaCalorAsync(materiaId, email, ct);
        if (!mapa.EsOk) return Resultado<AnalisisCurso>.Falla(mapa.Codigo, mapa.Mensaje ?? "No se pudo obtener el mapa de calor.");

        var i = ind.Valor!;
        var m = mapa.Valor!;

        var temas = m.Temas.Select(t =>
        {
            var celda = m.PromedioCursoTemas.TryGetValue(t.TemaId, out var c) ? c : new CeldaDto(null, "SinDatos");
            var conDatos = m.Estudiantes.Count(f => f.Temas.TryGetValue(t.TemaId, out var x) && x.Nota.HasValue);
            var criticos = m.Estudiantes.Count(f => f.Temas.TryGetValue(t.TemaId, out var x) && x.Nivel == "Critico");
            return new TemaResumen(t.TemaId, t.Tema, t.Unidad, celda.Nota, celda.Nivel, criticos, conDatos);
        }).OrderBy(t => t.Promedio ?? 11m).ThenBy(t => t.Tema).ToList();

        var actividades = i.Actividades
            .Select(a => new ActividadProblema(a.Nombre, a.Promedio, a.Calificadas, a.Ceros, a.SinEntrega, a.Pendientes))
            .ToList();

        return Resultado<AnalisisCurso>.Ok(new AnalisisCurso(
            materiaId, i.Materia, i.ActualizadoUtc, i.Umbrales, i.Curso, temas, actividades, m.ActividadesSinMapear, m.Temas.Count > 0));
    }
}

/// <summary>
/// Hallazgos y recomendaciones grupales basados en reglas (RN-09). Son la base del reporte y el respaldo del agente
/// cuando el modelo de lenguaje no está disponible. Todas las cifras salen de AnalisisCurso.
/// </summary>
public static class ReglasCurso
{
    private static string F(decimal? v) => v is null ? "—" : CalculadoraIndicadores.Fmt(v.Value);

    private static string Lista(IEnumerable<string> items)
    {
        var l = items.ToList();
        return l.Count switch { 0 => "", 1 => l[0], _ => string.Join(", ", l.Take(l.Count - 1)) + " y " + l[^1] };
    }

    private static IEnumerable<TemaResumen> TemasPor(AnalisisCurso a, string nivel) =>
        a.Temas.Where(t => t.Nivel == nivel && t.Promedio.HasValue);

    private static IEnumerable<ActividadProblema> SinEntregaAlta(AnalisisCurso a)
    {
        var umbral = Math.Max(1, (int)Math.Ceiling(a.Curso.Estudiantes * 0.3));
        return a.Actividades.Where(x => x.SinEntrega >= umbral).OrderByDescending(x => x.SinEntrega).Take(2);
    }

    public static IReadOnlyList<string> Hallazgos(AnalisisCurso a)
    {
        var c = a.Curso;
        var u = a.Umbrales;
        if (c.Estudiantes == 0) return new[] { "La materia no tiene estudiantes con datos." };
        if (c.SinDatos == c.Estudiantes) return new[] { "Aún no hay calificaciones evaluadas: no se pueden calcular hallazgos." };

        var h = new List<string>
        {
            $"Grupos diferenciados: {c.Aprobados} estudiante(s) con notas ≥ {F(u.Aprobado)}, {c.EnUmbral} en umbral y {c.Criticos} con notas < {F(u.Critico)}."
        };

        var criticos = TemasPor(a, "Critico").Take(3).ToList();
        var umbral = TemasPor(a, "EnUmbral").Take(3).ToList();
        if (criticos.Count > 0)
            h.Add("Temas críticos del curso: " + Lista(criticos.Select(t => $"«{t.Tema}» (promedio {F(t.Promedio)})")) + ".");
        else if (umbral.Count > 0)
            h.Add("Ningún tema está en nivel crítico; temas en umbral: " + Lista(umbral.Select(t => $"«{t.Tema}» ({F(t.Promedio)})")) + ".");
        else if (a.Temas.Any(t => t.Promedio.HasValue))
            h.Add("Ningún tema del curso está por debajo del mínimo de aprobación.");
        else if (!a.TieneEstructura)
            h.Add("La materia no tiene estructura temática: no se puede analizar el desempeño por tema.");
        else
            h.Add("Hay estructura temática, pero ninguna actividad evaluada está mapeada a ella.");

        foreach (var x in SinEntregaAlta(a))
            h.Add($"«{x.Nombre}»: {x.SinEntrega} de {c.Estudiantes} estudiantes sin entrega.");
        foreach (var x in a.Actividades.Where(x => x.Ceros > 0).OrderByDescending(x => x.Ceros).Take(2))
            h.Add($"«{x.Nombre}»: {x.Ceros} nota(s) cero (distinta de «sin entrega»).");

        if (c.PesoEvaluadoPromedio < 100m)
            h.Add($"Peso evaluado promedio: {F(c.PesoEvaluadoPromedio)} % del parcial; las conclusiones pueden cambiar cuando se califique el resto.");
        if (a.ActividadesSinMapear > 0)
            h.Add($"{a.ActividadesSinMapear} actividad(es) sin mapear a contenidos: no cuentan en el análisis por tema.");
        return h;
    }

    /// <summary>Límites de los datos que el agente debe reconocer siempre (HU-14, criterio 3).</summary>
    public static IReadOnlyList<string> Incertidumbre(AnalisisCurso a)
    {
        var c = a.Curso;
        var l = new List<string>();
        if (c.Estudiantes == 0) { l.Add("La materia no tiene estudiantes con datos."); return l; }
        if (c.SinDatos > 0) l.Add($"{c.SinDatos} estudiante(s) no tienen calificaciones evaluadas todavía y no pueden clasificarse.");
        if (c.PesoEvaluadoPromedio < 100m)
            l.Add($"Solo se ha evaluado en promedio el {F(c.PesoEvaluadoPromedio)} % del parcial: las notas finales pueden cambiar.");
        var pendientes = a.Actividades.Sum(x => x.Pendientes);
        if (pendientes > 0) l.Add($"Hay {pendientes} entrega(s) pendientes de calificar que aún no entran en los cálculos.");
        if (!a.TieneEstructura) l.Add("La materia no tiene estructura temática: no es posible analizar por tema ni recomendar contenidos.");
        else if (a.ActividadesSinMapear > 0) l.Add($"{a.ActividadesSinMapear} actividad(es) sin mapear a contenidos: el análisis por tema es parcial.");
        return l;
    }

    public static IReadOnlyList<string> Recomendaciones(AnalisisCurso a)
    {
        var c = a.Curso;
        var r = new List<string>();
        if (c.Estudiantes == 0 || c.SinDatos == c.Estudiantes)
            return new[] { "Sin recomendaciones: aún no hay calificaciones evaluadas." };

        var prioritarios = TemasPor(a, "Critico").Concat(TemasPor(a, "EnUmbral")).Take(2).ToList();
        foreach (var t in prioritarios)
            r.Add($"Clase de refuerzo sobre «{t.Tema}» con ejercicios guiados.");
        if (c.Criticos > 0)
            r.Add($"Tutoría para el grupo en riesgo crítico ({c.Criticos} estudiante(s)) antes del siguiente parcial.");
        else if (c.EnUmbral > 0)
            r.Add($"Seguimiento breve a los {c.EnUmbral} estudiante(s) en umbral para evitar que desciendan.");
        foreach (var x in SinEntregaAlta(a))
            r.Add($"Revisar con el grupo las causas de la falta de entrega en «{x.Nombre}».");
        if (r.Count == 0)
            r.Add("Sin acciones urgentes: mantener el seguimiento de las próximas actividades.");
        return r;
    }
}
