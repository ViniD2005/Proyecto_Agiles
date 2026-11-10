using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Domain;
using EduMonitor.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Analytics.Api.Services;

/// <summary>Consultas de MS-C: indicadores, riesgo, mapa de calor y ficha. Todas validan que la materia sea del docente (RN-07).</summary>
public class AnaliticaService(AnalyticsDbContext db, RecalculoService recalculo)
{
    public const string AvisoOrientativo =
        "Las alertas y niveles de riesgo son orientativos: no constituyen un diagnóstico y deben ser revisados por el docente.";

    private sealed record Datos(
        MateriaAnalitica Materia, UmbralesDto Umbrales,
        List<EstudianteAnalitico> Estudiantes, List<ActividadAnalitica> Actividades, List<CalificacionAnalitica> Calificaciones,
        List<UnidadAnalitica> Unidades, List<TemaAnalitico> Temas, List<ContenidoAnalitico> Contenidos, List<MapeoAnalitico> Mapeos,
        List<IndicadorEstudiante> Indicadores, List<AlertaRiesgo> Alertas)
    {
        public List<ActividadCalc> ActividadesCalc => Actividades.OrderBy(a => a.Orden)
            .Select(a => new ActividadCalc(a.ActividadId, a.Nombre, a.Tipo, a.Peso, a.Orden)).ToList();

        public Dictionary<int, IReadOnlyDictionary<int, NotaCalc>> NotasPorEstudiante => Calificaciones
            .GroupBy(c => c.EstudianteId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<int, NotaCalc>)g.ToDictionary(
                c => c.ActividadId, c => new NotaCalc(c.ActividadId, c.Nota, CalculadoraIndicadores.ParseEstado(c.Estado))));
    }

    private static string Nombre(EstudianteAnalitico e)
    {
        var n = $"{e.Nombres} {e.Apellidos}".Trim();
        return n.Length > 0 ? n : $"Estudiante {e.EstudianteId}";
    }

    private async Task<Resultado<Datos>> CargarAsync(int materiaId, string? email, CancellationToken ct)
    {
        var materia = await db.Materias.AsNoTracking().FirstOrDefaultAsync(m => m.Id == materiaId, ct);
        if (materia is null)
            return Resultado<Datos>.Falla(CodigoResultado.NoEncontrado,
                "La materia no tiene datos de análisis todavía. Sincronice las calificaciones desde «Mis materias».");
        if (string.IsNullOrWhiteSpace(email) || !string.Equals(materia.DocenteEmail, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return Resultado<Datos>.Falla(CodigoResultado.Prohibido, "No tienes permiso para analizar esta materia.");

        var (critico, aprobado, personalizado) = await recalculo.ObtenerUmbralesAsync(materiaId, ct);
        var datos = new Datos(
            materia, new UmbralesDto(critico, aprobado, personalizado),
            await db.Estudiantes.AsNoTracking().Where(x => x.MateriaId == materiaId).OrderBy(x => x.Apellidos).ThenBy(x => x.Nombres).ToListAsync(ct),
            await db.Actividades.AsNoTracking().Where(x => x.MateriaId == materiaId).ToListAsync(ct),
            await db.Calificaciones.AsNoTracking().Where(x => x.MateriaId == materiaId).ToListAsync(ct),
            await db.Unidades.AsNoTracking().Where(x => x.MateriaId == materiaId).OrderBy(x => x.Orden).ToListAsync(ct),
            await db.Temas.AsNoTracking().Where(x => x.MateriaId == materiaId).OrderBy(x => x.Orden).ToListAsync(ct),
            await db.Contenidos.AsNoTracking().Where(x => x.MateriaId == materiaId).OrderBy(x => x.Orden).ToListAsync(ct),
            await db.Mapeos.AsNoTracking().Where(x => x.MateriaId == materiaId).ToListAsync(ct),
            await db.Indicadores.AsNoTracking().Where(x => x.MateriaId == materiaId).ToListAsync(ct),
            await db.Alertas.AsNoTracking().Where(x => x.MateriaId == materiaId).OrderBy(x => x.Id).ToListAsync(ct));
        return Resultado<Datos>.Ok(datos);
    }

    private static Resultado<T> Propagar<T>(Resultado<Datos> r) => Resultado<T>.Falla(r.Codigo, r.Mensaje ?? "Error.");

    private static int OrdenNivel(string nivel) => nivel switch
    {
        nameof(NivelRiesgo.Critico) => 0, nameof(NivelRiesgo.EnUmbral) => 1, nameof(NivelRiesgo.SinDatos) => 2, _ => 3
    };

    private static AlertaDto ADto(AlertaRiesgo a) => new(a.Tipo, a.Regla, a.Evidencia, a.Actividad, a.Valor);

    // ============================================================ HU-10 indicadores
    public async Task<Resultado<IndicadoresDto>> ObtenerIndicadoresAsync(int materiaId, string? email, CancellationToken ct = default)
    {
        var r = await CargarAsync(materiaId, email, ct);
        if (!r.EsOk) return Propagar<IndicadoresDto>(r);
        var d = r.Valor!;

        var indicadores = d.Indicadores.ToDictionary(i => i.EstudianteId);
        var alertas = d.Alertas.ToLookup(a => a.EstudianteId);

        var estudiantes = d.Estudiantes.Select(e =>
        {
            indicadores.TryGetValue(e.EstudianteId, out var ind);
            var lista = alertas[e.EstudianteId].ToList();
            return new IndicadorEstudianteDto(
                e.EstudianteId, Nombre(e), ind?.NotaPonderada, ind?.NotaAcumulada ?? 0m, ind?.PesoEvaluado ?? 0m,
                ind?.Nivel ?? nameof(NivelRiesgo.SinDatos), lista.Count, lista.FirstOrDefault()?.Evidencia);
        }).OrderBy(e => OrdenNivel(e.Nivel)).ThenBy(e => e.NotaPonderada ?? 11m).ThenBy(e => e.Nombre).ToList();

        var notas = estudiantes.Where(e => e.NotaPonderada.HasValue).Select(e => e.NotaPonderada!.Value).ToList();
        var curso = new IndicadorCursoDto(
            CalculadoraIndicadores.Promedio(notas), CalculadoraIndicadores.DesviacionMuestral(notas), estudiantes.Count,
            estudiantes.Count(e => e.Nivel == nameof(NivelRiesgo.Critico)),
            estudiantes.Count(e => e.Nivel == nameof(NivelRiesgo.EnUmbral)),
            estudiantes.Count(e => e.Nivel == nameof(NivelRiesgo.Aprobado)),
            estudiantes.Count(e => e.Nivel == nameof(NivelRiesgo.SinDatos)),
            estudiantes.Count == 0 ? 0m : CalculadoraIndicadores.Redondear(estudiantes.Average(e => e.PesoEvaluado)),
            CalculadoraIndicadores.Distribucion(notas).Select(x => new DistribucionDto(x.Rango, x.Cantidad)).ToList());

        var actividades = d.Actividades.OrderBy(a => a.Orden).Select(a =>
        {
            var filas = d.Calificaciones.Where(c => c.ActividadId == a.ActividadId).ToList();
            var evaluadas = filas.Where(c => CalculadoraIndicadores.EsEvaluada(CalculadoraIndicadores.ParseEstado(c.Estado)) && c.Nota.HasValue)
                .Select(c => c.Nota!.Value).ToList();
            var ceros = filas.Count(c => c.Estado == EduMonitor.Common.Messaging.EstadosCalificacion.Cero);
            var pendientes = filas.Count(c => c.Estado == EduMonitor.Common.Messaging.EstadosCalificacion.PendienteDeCalificar);
            var sinEntrega = d.Estudiantes.Count - evaluadas.Count - pendientes;
            return new IndicadorActividadDto(
                a.ActividadId, a.Nombre, a.Tipo, a.Peso, CalculadoraIndicadores.Promedio(evaluadas), CalculadoraIndicadores.DesviacionMuestral(evaluadas),
                evaluadas.Count - ceros, ceros, Math.Max(0, sinEntrega), pendientes,
                CalculadoraIndicadores.Distribucion(evaluadas).Select(x => new DistribucionDto(x.Rango, x.Cantidad)).ToList());
        }).ToList();

        var actualizado = new[] { d.Materia.UltimasCalificacionesUtc, d.Materia.UltimoMapeoUtc }.Max();
        return Resultado<IndicadoresDto>.Ok(new IndicadoresDto(
            materiaId, d.Materia.Nombre, d.Materia.UltimasCalificacionesUtc ?? actualizado, "0–10", d.Umbrales, curso, actividades, estudiantes));
    }

    // ============================================================ HU-11 riesgo
    public async Task<Resultado<RiesgoDto>> ObtenerRiesgoAsync(int materiaId, string? email, CancellationToken ct = default)
    {
        var r = await CargarAsync(materiaId, email, ct);
        if (!r.EsOk) return Propagar<RiesgoDto>(r);
        var d = r.Valor!;
        var indicadores = d.Indicadores.ToDictionary(i => i.EstudianteId);
        var alertas = d.Alertas.ToLookup(a => a.EstudianteId);

        var lista = d.Estudiantes.Select(e =>
        {
            indicadores.TryGetValue(e.EstudianteId, out var ind);
            return new EstudianteRiesgoDto(e.EstudianteId, Nombre(e), ind?.NotaPonderada,
                ind?.Nivel ?? nameof(NivelRiesgo.SinDatos), alertas[e.EstudianteId].Select(ADto).ToList());
        }).OrderBy(e => OrdenNivel(e.Nivel)).ThenBy(e => e.NotaPonderada ?? 11m).ThenBy(e => e.Nombre).ToList();

        return Resultado<RiesgoDto>.Ok(new RiesgoDto(materiaId, d.Umbrales, AvisoOrientativo, lista));
    }

    /// <summary>HU-11, criterio 2: al guardar nuevos umbrales se recalcula la clasificación y las alertas.</summary>
    public async Task<Resultado<RiesgoDto>> ActualizarUmbralesAsync(ActualizarUmbralesRequest? req, string? email, CancellationToken ct = default)
    {
        if (req is null) return Resultado<RiesgoDto>.Falla(CodigoResultado.Invalido, "Faltan los datos de los umbrales.");
        if (req.Critico <= 0m || req.Critico >= req.Aprobado || req.Aprobado > 10m)
            return Resultado<RiesgoDto>.Falla(CodigoResultado.Invalido,
                "Los umbrales deben cumplir 0 < crítico < aprobado ≤ 10 (por defecto 5 y 7).");

        var materia = await db.Materias.AsNoTracking().FirstOrDefaultAsync(m => m.Id == req.MateriaId, ct);
        if (materia is null)
            return Resultado<RiesgoDto>.Falla(CodigoResultado.NoEncontrado, "La materia no tiene datos de análisis todavía.");
        if (string.IsNullOrWhiteSpace(email) || !string.Equals(materia.DocenteEmail, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return Resultado<RiesgoDto>.Falla(CodigoResultado.Prohibido, "No tienes permiso para configurar esta materia.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var umbral = await db.Umbrales.FirstOrDefaultAsync(u => u.MateriaId == req.MateriaId, ct);
        if (umbral is null)
        {
            umbral = new UmbralMateria { MateriaId = req.MateriaId };
            db.Umbrales.Add(umbral);
        }
        umbral.Critico = req.Critico;
        umbral.Aprobado = req.Aprobado;
        umbral.ActualizadoUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await recalculo.RecalcularAsync(req.MateriaId, ct);
        await tx.CommitAsync(ct);

        return await ObtenerRiesgoAsync(req.MateriaId, email, ct);
    }

    // ============================================================ HU-12 mapa de calor
    public async Task<Resultado<MapaCalorDto>> ObtenerMapaCalorAsync(int materiaId, string? email, CancellationToken ct = default)
    {
        var r = await CargarAsync(materiaId, email, ct);
        if (!r.EsOk) return Propagar<MapaCalorDto>(r);
        var d = r.Valor!;
        var (critico, aprobado) = (d.Umbrales.Critico, d.Umbrales.Aprobado);

        var actividades = d.ActividadesCalc.ToDictionary(a => a.Id);
        var notasPorEst = d.NotasPorEstudiante;
        var actPorContenido = d.Mapeos.GroupBy(m => m.ContenidoId).ToDictionary(g => g.Key, g => g.Select(m => m.ActividadId).ToList());
        var contenidosPorTema = d.Contenidos.GroupBy(c => c.TemaId).ToDictionary(g => g.Key, g => g.ToList());
        var unidades = d.Unidades.ToDictionary(u => u.UnidadId);

        var temasOrdenados = d.Temas
            .OrderBy(t => unidades.TryGetValue(t.UnidadId, out var u) ? u.Orden : int.MaxValue).ThenBy(t => t.Orden).ToList();
        var columnas = temasOrdenados.Select(t => new TemaColumnaDto(
            t.TemaId, t.Nombre, t.UnidadId, unidades.TryGetValue(t.UnidadId, out var u) ? u.Nombre : "—",
            contenidosPorTema.GetValueOrDefault(t.TemaId, new List<ContenidoAnalitico>())
                .OrderBy(c => c.Orden).Select(c => new ContenidoColumnaDto(c.ContenidoId, c.Nombre)).ToList())).ToList();

        CeldaDto Celda(decimal? nota) => new(nota, CalculadoraIndicadores.NivelDe(nota, critico, aprobado).ToString());
        List<int> ActividadesDeTema(int temaId) => contenidosPorTema.GetValueOrDefault(temaId, new List<ContenidoAnalitico>())
            .SelectMany(c => actPorContenido.GetValueOrDefault(c.ContenidoId, new List<int>())).Distinct().ToList();

        var filas = new List<FilaMapaDto>();
        foreach (var e in d.Estudiantes)
        {
            var notas = notasPorEst.GetValueOrDefault(e.EstudianteId) ?? new Dictionary<int, NotaCalc>();
            var porTema = temasOrdenados.ToDictionary(t => t.TemaId,
                t => Celda(CalculadoraIndicadores.DesempenoGrupo(ActividadesDeTema(t.TemaId), actividades, notas)));
            var porContenido = d.Contenidos.ToDictionary(c => c.ContenidoId,
                c => Celda(CalculadoraIndicadores.DesempenoGrupo(actPorContenido.GetValueOrDefault(c.ContenidoId, new List<int>()), actividades, notas)));
            filas.Add(new FilaMapaDto(e.EstudianteId, Nombre(e), porTema, porContenido));
        }

        CeldaDto PromedioCurso(Func<FilaMapaDto, CeldaDto?> selector) =>
            Celda(CalculadoraIndicadores.Promedio(filas.Select(selector).Where(c => c?.Nota is not null).Select(c => c!.Nota!.Value).ToList()));

        var promTemas = temasOrdenados.ToDictionary(t => t.TemaId, t => PromedioCurso(f => f.Temas.GetValueOrDefault(t.TemaId)));
        var promContenidos = d.Contenidos.ToDictionary(c => c.ContenidoId, c => PromedioCurso(f => f.Contenidos.GetValueOrDefault(c.ContenidoId)));
        var mapeadas = d.Mapeos.Select(m => m.ActividadId).ToHashSet();
        var sinMapear = d.Actividades.Count(a => !mapeadas.Contains(a.ActividadId));

        return Resultado<MapaCalorDto>.Ok(new MapaCalorDto(materiaId, d.Umbrales, columnas, filas, promTemas, promContenidos, sinMapear));
    }

    // ============================================================ HU-12 ficha del estudiante
    public async Task<Resultado<FichaEstudianteDto>> ObtenerFichaAsync(int materiaId, int estudianteId, string? email, CancellationToken ct = default)
    {
        var r = await CargarAsync(materiaId, email, ct);
        if (!r.EsOk) return Propagar<FichaEstudianteDto>(r);
        var d = r.Valor!;
        var est = d.Estudiantes.FirstOrDefault(e => e.EstudianteId == estudianteId);
        if (est is null)
            return Resultado<FichaEstudianteDto>.Falla(CodigoResultado.NoEncontrado, "El estudiante no pertenece a esta materia.");

        var (critico, aprobado) = (d.Umbrales.Critico, d.Umbrales.Aprobado);
        var actividadesCalc = d.ActividadesCalc;
        var actividades = actividadesCalc.ToDictionary(a => a.Id);
        var notas = d.NotasPorEstudiante.GetValueOrDefault(estudianteId) ?? new Dictionary<int, NotaCalc>();
        var ind = d.Indicadores.FirstOrDefault(i => i.EstudianteId == estudianteId);

        var unidades = d.Unidades.ToDictionary(u => u.UnidadId);
        var temas = d.Temas.ToDictionary(t => t.TemaId);
        var contenidos = d.Contenidos.ToDictionary(c => c.ContenidoId);
        var actPorContenido = d.Mapeos.GroupBy(m => m.ContenidoId).ToDictionary(g => g.Key, g => g.Select(m => m.ActividadId).ToList());

        // HU-12, criterio 2: a cada nota se le muestran los temas y contenidos que evalúa
        var filasActividad = actividadesCalc.Select(a =>
        {
            var relaciones = d.Mapeos.Where(m => m.ActividadId == a.Id)
                .Where(m => contenidos.ContainsKey(m.ContenidoId))
                .Select(m => contenidos[m.ContenidoId])
                .Where(c => temas.ContainsKey(c.TemaId))
                .OrderBy(c => c.Orden)
                .Select(c => new RelacionFichaDto(
                    unidades.TryGetValue(temas[c.TemaId].UnidadId, out var u) ? u.Nombre : "—", temas[c.TemaId].Nombre, c.Nombre))
                .ToList();
            var nota = notas.TryGetValue(a.Id, out var n) ? n : new NotaCalc(a.Id, null, EstadoNota.SinEntrega);
            return new ActividadFichaDto(a.Id, a.Nombre, a.Tipo, a.Peso, nota.Nota, nota.Estado.ToString(), relaciones);
        }).ToList();

        var evolucion = actividadesCalc.Where(a => notas.TryGetValue(a.Id, out var n) && CalculadoraIndicadores.EsEvaluada(n.Estado) && n.Nota.HasValue)
            .Select(a => new EvolucionDto(a.Nombre, notas[a.Id].Nota!.Value)).ToList();

        var temasFicha = d.Temas
            .OrderBy(t => unidades.TryGetValue(t.UnidadId, out var u) ? u.Orden : int.MaxValue).ThenBy(t => t.Orden)
            .Select(t =>
            {
                var ids = d.Contenidos.Where(c => c.TemaId == t.TemaId)
                    .SelectMany(c => actPorContenido.GetValueOrDefault(c.ContenidoId, new List<int>())).Distinct();
                var nota = CalculadoraIndicadores.DesempenoGrupo(ids, actividades, notas);
                return new TemaFichaDto(t.TemaId, t.Nombre, unidades.TryGetValue(t.UnidadId, out var u) ? u.Nombre : "—",
                    nota, CalculadoraIndicadores.NivelDe(nota, critico, aprobado).ToString());
            }).ToList();

        return Resultado<FichaEstudianteDto>.Ok(new FichaEstudianteDto(
            materiaId, d.Materia.Nombre, estudianteId, Nombre(est), ind?.NotaPonderada, ind?.NotaAcumulada ?? 0m, ind?.PesoEvaluado ?? 0m,
            ind?.Nivel ?? nameof(NivelRiesgo.SinDatos), d.Umbrales, filasActividad, evolucion, temasFicha,
            d.Alertas.Where(a => a.EstudianteId == estudianteId).Select(ADto).ToList(), AvisoOrientativo));
    }
}
