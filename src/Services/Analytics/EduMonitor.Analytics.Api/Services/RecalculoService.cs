using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Analytics.Api.Services;

/// <summary>
/// Recalcula indicadores y alertas de una materia con las reglas deterministas (RN-09).
/// Se ejecuta al llegar CalificacionesSincronizadas y al cambiar los umbrales (HU-10 criterio 4, HU-11 criterio 2).
/// </summary>
public class RecalculoService(AnalyticsDbContext db, ILogger<RecalculoService> logger)
{
    public async Task<(decimal Critico, decimal Aprobado, bool Personalizado)> ObtenerUmbralesAsync(int materiaId, CancellationToken ct = default)
    {
        var u = await db.Umbrales.AsNoTracking().FirstOrDefaultAsync(x => x.MateriaId == materiaId, ct);
        return u is null
            ? (CalculadoraIndicadores.CriticoPorDefecto, CalculadoraIndicadores.AprobadoPorDefecto, false)
            : (u.Critico, u.Aprobado, true);
    }

    public async Task RecalcularAsync(int materiaId, CancellationToken ct = default)
    {
        var (critico, aprobado, _) = await ObtenerUmbralesAsync(materiaId, ct);

        var estudiantes = await db.Estudiantes.AsNoTracking().Where(e => e.MateriaId == materiaId).ToListAsync(ct);
        var actividades = await db.Actividades.AsNoTracking().Where(a => a.MateriaId == materiaId).OrderBy(a => a.Orden).ToListAsync(ct);
        var calificaciones = await db.Calificaciones.AsNoTracking().Where(c => c.MateriaId == materiaId).ToListAsync(ct);

        var calcActividades = actividades.Select(a => new ActividadCalc(a.ActividadId, a.Nombre, a.Tipo, a.Peso, a.Orden)).ToList();
        var porEstudiante = calificaciones.GroupBy(c => c.EstudianteId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyDictionary<int, NotaCalc>)g.ToDictionary(c => c.ActividadId,
                c => new NotaCalc(c.ActividadId, c.Nota, CalculadoraIndicadores.ParseEstado(c.Estado))));

        var ahora = DateTime.UtcNow;
        var indicadores = new List<IndicadorEstudiante>();
        var alertas = new List<AlertaRiesgo>();
        foreach (var est in estudiantes)
        {
            var notas = porEstudiante.GetValueOrDefault(est.EstudianteId) ?? new Dictionary<int, NotaCalc>();
            var resultado = CalculadoraIndicadores.CalcularNotaEstudiante(calcActividades, notas);
            var nivel = CalculadoraIndicadores.NivelDe(resultado.NotaPonderada, critico, aprobado);

            indicadores.Add(new IndicadorEstudiante
            {
                MateriaId = materiaId, EstudianteId = est.EstudianteId,
                NotaPonderada = resultado.NotaPonderada, NotaAcumulada = resultado.NotaAcumulada,
                PesoEvaluado = resultado.PesoEvaluado, Nivel = nivel.ToString(), CalculadoUtc = ahora
            });

            foreach (var a in CalculadoraIndicadores.GenerarAlertas(calcActividades, notas, critico))
                alertas.Add(new AlertaRiesgo
                {
                    MateriaId = materiaId, EstudianteId = est.EstudianteId, Tipo = a.Tipo, Regla = a.Regla,
                    Evidencia = a.Evidencia, Actividad = a.Actividad, Valor = a.Valor, CreadaUtc = ahora
                });
        }

        await db.Indicadores.Where(i => i.MateriaId == materiaId).ExecuteDeleteAsync(ct);
        await db.Alertas.Where(a => a.MateriaId == materiaId).ExecuteDeleteAsync(ct);
        db.ChangeTracker.Clear();   // los borrados masivos no actualizan las entidades ya rastreadas
        db.Indicadores.AddRange(indicadores);
        db.Alertas.AddRange(alertas);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Materia {Materia}: {Estudiantes} indicadores y {Alertas} alertas recalculados (umbrales {Critico}/{Aprobado})",
            materiaId, indicadores.Count, alertas.Count, critico, aprobado);
    }
}
