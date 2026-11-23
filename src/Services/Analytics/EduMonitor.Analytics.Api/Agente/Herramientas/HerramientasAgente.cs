using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Services;
using Microsoft.EntityFrameworkCore;
using static EduMonitor.Analytics.Api.Agente.Herramientas.HerramientasUtil;

namespace EduMonitor.Analytics.Api.Agente.Herramientas;

/// <summary>Parámetro materiaId, común a casi todas las herramientas.</summary>
internal static class Parametro
{
    public static readonly ParametroHerramienta MateriaId = new("materiaId", TipoParametro.Entero, true, "Id de la materia (debe ser del docente).");
}

/// <summary>1) Cursos que el docente puede consultar. El agente no puede salir de esta lista.</summary>
public sealed class ListarCursosAutorizadosTool(AnalyticsDbContext db) : IHerramienta
{
    public string Nombre => "listar_cursos_autorizados";
    public string Descripcion => "Lista las materias del docente autenticado.";
    public IReadOnlyList<ParametroHerramienta> Parametros => Array.Empty<ParametroHerramienta>();

    public async Task<ResultadoHerramienta> EjecutarAsync(ContextoHerramienta ctx, IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        var email = ctx.DocenteEmail.Trim().ToLowerInvariant();
        var cursos = await db.Materias.AsNoTracking().Where(m => m.DocenteEmail.ToLower() == email).OrderBy(m => m.Nombre).ToListAsync(ct);
        if (cursos.Count == 0) return ResultadoHerramienta.Falla("El docente no tiene materias sincronizadas.");
        var nombres = string.Join(", ", cursos.Select(c => $"«{c.Nombre}»"));
        return ResultadoHerramienta.Ok($"{cursos.Count} curso(s) autorizado(s)",
            new[] { $"Cursos autorizados del docente: {nombres}." },
            cursos.Select(c => new { materiaId = c.Id, nombre = c.Nombre }).ToList());
    }
}

/// <summary>2) Notas ponderadas por estudiante y estadística del curso (mismos números del dashboard).</summary>
public sealed class ObtenerCalificacionesTool(AnaliticaService analitica) : IHerramienta
{
    public string Nombre => "obtener_calificaciones";
    public string Descripcion => "Nota ponderada, nivel de riesgo y estadística del curso.";
    public IReadOnlyList<ParametroHerramienta> Parametros => new[] { Parametro.MateriaId };

    public async Task<ResultadoHerramienta> EjecutarAsync(ContextoHerramienta ctx, IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        var r = await analitica.ObtenerIndicadoresAsync(Entero(args, "materiaId"), ctx.DocenteEmail, ct);
        if (!r.EsOk) return ResultadoHerramienta.DeResultado(r, "las calificaciones");
        var d = r.Valor!;
        var c = d.Curso;

        var hechos = new List<string>
        {
            $"«{d.Materia}»: promedio {F(c.Promedio)} (desviación estándar {F(c.Desviacion)}) con {c.Estudiantes} estudiante(s), escala {d.Escala}.",
            $"Niveles: {c.Criticos} crítico(s), {c.EnUmbral} en umbral, {c.Aprobados} aprobado(s), {c.SinDatos} sin datos (umbrales {F(d.Umbrales.Critico)} y {F(d.Umbrales.Aprobado)})."
        };
        var bajos = d.Estudiantes.Where(e => e.NotaPonderada.HasValue).OrderBy(e => e.NotaPonderada).Take(5).ToList();
        if (bajos.Count > 0)
            hechos.Add("Notas ponderadas más bajas: " + string.Join(" · ", bajos.Select(e => $"{Marcador.Est(e.EstudianteId)} {F(e.NotaPonderada)}")) + ".");

        return ResultadoHerramienta.Ok($"{c.Estudiantes} estudiantes, promedio {F(c.Promedio)}", hechos, new
        {
            materia = d.Materia,
            umbrales = new { critico = d.Umbrales.Critico, aprobado = d.Umbrales.Aprobado },
            promedio = c.Promedio, desviacion = c.Desviacion,
            estudiantes = d.Estudiantes.Select(e => new
            {
                estudiante = Marcador.Est(e.EstudianteId), nota = e.NotaPonderada, nivel = e.Nivel, pesoEvaluado = e.PesoEvaluado
            }).ToList()
        });
    }
}

/// <summary>3) Alertas con evidencia (RF-07). Opcionalmente filtradas por nivel.</summary>
public sealed class IdentificarAlertasTool(AnaliticaService analitica) : IHerramienta
{
    public string Nombre => "identificar_alertas";
    public string Descripcion => "Estudiantes con alertas y la evidencia de cada alerta.";
    public IReadOnlyList<ParametroHerramienta> Parametros => new[]
    {
        Parametro.MateriaId,
        new ParametroHerramienta("nivel", TipoParametro.Nivel, false, "Critico, EnUmbral o Aprobado. Sin valor: crítico y en umbral.")
    };

    public async Task<ResultadoHerramienta> EjecutarAsync(ContextoHerramienta ctx, IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        var r = await analitica.ObtenerRiesgoAsync(Entero(args, "materiaId"), ctx.DocenteEmail, ct);
        if (!r.EsOk) return ResultadoHerramienta.DeResultado(r, "las alertas");
        var d = r.Valor!;

        args.TryGetValue("nivel", out var nivel);
        var seleccion = d.Estudiantes
            .Where(e => nivel is not null ? e.Nivel == nivel : e.Nivel is "Critico" or "EnUmbral")
            .Where(e => e.Alertas.Count > 0 || e.Nivel == "Critico")
            .ToList();
        var etiqueta = nivel switch { "Critico" => "nivel crítico", "EnUmbral" => "nivel en umbral", "Aprobado" => "nivel aprobado", _ => "riesgo (crítico o en umbral)" };

        if (seleccion.Count == 0)
            return ResultadoHerramienta.Ok("Sin estudiantes con alertas", new[] { $"No hay estudiantes en {etiqueta} con alertas." }, new { estudiantes = Array.Empty<object>() });

        var hechos = new List<string>
        {
            $"{seleccion.Count} estudiante(s) en {etiqueta}: " +
            string.Join(", ", seleccion.Take(8).Select(e => $"{Marcador.Est(e.EstudianteId)} ({F(e.NotaPonderada)})")) +
            (seleccion.Count > 8 ? $" y {seleccion.Count - 8} más." : ".")
        };
        var porTipo = seleccion.SelectMany(e => e.Alertas).GroupBy(a => a.Tipo).OrderByDescending(g => g.Count()).ToList();
        if (porTipo.Count > 0)
            hechos.Add("Alertas por tipo: " + string.Join(", ", porTipo.Select(g => $"{g.Key} {g.Count()}")) + ".");
        // una línea por estudiante con la actividad concreta que no entregó
        foreach (var e in seleccion.Where(x => x.Alertas.Any(a => a.Tipo == "SinEntrega")).Take(8))
            hechos.Add($"{Marcador.Est(e.EstudianteId)}: {e.Alertas.First(a => a.Tipo == "SinEntrega").Evidencia}");
        foreach (var e in seleccion.Where(x => x.Alertas.Count > 0).Take(3))
            hechos.Add($"{Marcador.Est(e.EstudianteId)}: {e.Alertas[0].Evidencia}");

        return ResultadoHerramienta.Ok($"{seleccion.Count} estudiante(s) con alertas", hechos, new
        {
            nivel = nivel ?? "Critico+EnUmbral",
            estudiantes = seleccion.Select(e => new
            {
                estudiante = Marcador.Est(e.EstudianteId), nota = e.NotaPonderada, nivel = e.Nivel,
                alertas = e.Alertas.Select(a => new { tipo = a.Tipo, evidencia = a.Evidencia }).ToList()
            }).ToList()
        });
    }
}

/// <summary>4) Resumen del curso: hallazgos calculados por reglas (el modelo solo los explica).</summary>
public sealed class GenerarResumenTool(AnalisisCursoService analisis) : IHerramienta
{
    public string Nombre => "generar_resumen";
    public string Descripcion => "Hallazgos y datos faltantes del curso, calculados por el sistema.";
    public IReadOnlyList<ParametroHerramienta> Parametros => new[] { Parametro.MateriaId };

    public async Task<ResultadoHerramienta> EjecutarAsync(ContextoHerramienta ctx, IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        var r = await analisis.ObtenerAsync(Entero(args, "materiaId"), ctx.DocenteEmail, ct);
        if (!r.EsOk) return ResultadoHerramienta.DeResultado(r, "el resumen del curso");
        var a = r.Valor!;
        var hallazgos = ReglasCurso.Hallazgos(a);
        return ResultadoHerramienta.Ok($"{hallazgos.Count} hallazgo(s)", hallazgos.Take(5).ToList(), new
        {
            curso = new { a.Curso.Promedio, a.Curso.Desviacion, a.Curso.Estudiantes, a.Curso.Criticos, a.Curso.EnUmbral, a.Curso.Aprobados, a.Curso.SinDatos },
            hallazgos,
            actividadesSinMapear = a.ActividadesSinMapear
        });
    }
}

/// <summary>5) Desempeño del curso por tema (mapa de calor, HU-12).</summary>
public sealed class ObtenerDesempenoTemasTool(AnalisisCursoService analisis) : IHerramienta
{
    public string Nombre => "obtener_desempeno_temas";
    public string Descripcion => "Promedio del curso por tema y cuántos estudiantes están en nivel crítico en cada uno.";
    public IReadOnlyList<ParametroHerramienta> Parametros => new[] { Parametro.MateriaId };

    public async Task<ResultadoHerramienta> EjecutarAsync(ContextoHerramienta ctx, IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        var r = await analisis.ObtenerAsync(Entero(args, "materiaId"), ctx.DocenteEmail, ct);
        if (!r.EsOk) return ResultadoHerramienta.DeResultado(r, "el desempeño por tema");
        var a = r.Valor!;
        var conDatos = a.Temas.Where(t => t.Promedio.HasValue).ToList();
        if (conDatos.Count == 0)
            return ResultadoHerramienta.Ok("Sin datos por tema",
                new[] { a.TieneEstructura
                    ? "No hay notas evaluadas mapeadas a los temas: no se puede analizar por tema."
                    : "La materia no tiene estructura temática: no se puede analizar por tema." },
                new { temas = Array.Empty<object>() });

        var hechos = new List<string>
        {
            "Temas con menor promedio del curso: " + string.Join("; ", conDatos.Take(4).Select(t =>
                $"«{t.Tema}» {F(t.Promedio)} ({NivelTexto(t.Nivel)}, {t.EstudiantesCriticos} estudiante(s) crítico(s))")) + "."
        };
        return ResultadoHerramienta.Ok($"{conDatos.Count} tema(s) con datos", hechos, new
        {
            temas = a.Temas.Select(t => new { tema = t.Tema, unidad = t.Unidad, promedio = t.Promedio, nivel = t.Nivel, estudiantesCriticos = t.EstudiantesCriticos }).ToList()
        });
    }

    internal static string NivelTexto(string n) => n switch { "Critico" => "crítico", "EnUmbral" => "en umbral", "Aprobado" => "aprobado", _ => "sin datos" };
}

/// <summary>6) Ficha de un estudiante (solo cuando el docente lo menciona en la consulta).</summary>
public sealed class ObtenerFichaEstudianteTool(AnaliticaService analitica) : IHerramienta
{
    public string Nombre => "obtener_ficha_estudiante";
    public string Descripcion => "Nota, temas débiles, alertas y tendencia de un estudiante del curso.";
    public IReadOnlyList<ParametroHerramienta> Parametros => new[]
    {
        Parametro.MateriaId,
        new ParametroHerramienta("estudianteId", TipoParametro.Entero, true, "Id del estudiante dentro de la materia.")
    };

    public async Task<ResultadoHerramienta> EjecutarAsync(ContextoHerramienta ctx, IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        var estudianteId = Entero(args, "estudianteId");
        var r = await analitica.ObtenerFichaAsync(Entero(args, "materiaId"), estudianteId, ctx.DocenteEmail, ct);
        if (!r.EsOk) return ResultadoHerramienta.DeResultado(r, "la ficha del estudiante");
        var f = r.Valor!;
        var marca = Marcador.Est(estudianteId);

        var hechos = new List<string>
        {
            $"{marca}: nota ponderada {F(f.NotaPonderada)} ({ObtenerDesempenoTemasTool.NivelTexto(f.Nivel)}), {F(f.PesoEvaluado)} % del parcial evaluado."
        };
        var debiles = f.Temas.Where(t => t.Nota.HasValue && t.Nivel != "Aprobado").OrderBy(t => t.Nota).Take(3).ToList();
        if (debiles.Count > 0)
            hechos.Add($"{marca}: temas más débiles: " + string.Join("; ", debiles.Select(t => $"«{t.Tema}» {F(t.Nota)}")) + ".");
        foreach (var a in f.Alertas.Take(2)) hechos.Add($"{marca}: {a.Evidencia}");
        if (f.Tendencia.Disponible) hechos.Add($"{marca}: {f.Tendencia.Mensaje} (estimación, no definitiva).");

        return ResultadoHerramienta.Ok($"Ficha de un estudiante, nota {F(f.NotaPonderada)}", hechos, new
        {
            estudiante = marca, nota = f.NotaPonderada, nivel = f.Nivel, pesoEvaluado = f.PesoEvaluado,
            temas = f.Temas.Select(t => new { tema = t.Tema, nota = t.Nota, nivel = t.Nivel }).ToList(),
            alertas = f.Alertas.Select(a => a.Evidencia).ToList(),
            tendencia = new { f.Tendencia.Disponible, f.Tendencia.Direccion, f.Tendencia.Proyeccion }
        });
    }
}
