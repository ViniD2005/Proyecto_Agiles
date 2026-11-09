using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Domain;
using EduMonitor.Common.Messaging;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Services;

/// <summary>
/// Arma las «fotos» que MS-B envía a MS-C por RabbitMQ. Solo viajan los datos mínimos
/// (sin correos de estudiantes), según RNF-02.
/// </summary>
public class EventSnapshotBuilder(AcademicDbContext db)
{
    public async Task<CalificacionesSincronizadas> ConstruirCalificacionesAsync(Materia materia, CancellationToken ct = default)
    {
        var estudiantes = await db.Matriculas.AsNoTracking()
            .Where(m => m.MateriaId == materia.Id && m.Activa)
            .Select(m => m.Estudiante!)
            .OrderBy(e => e.Apellidos).ThenBy(e => e.Nombres)
            .ToListAsync(ct);

        var actividades = await db.Actividades.AsNoTracking()
            .Where(a => a.MateriaId == materia.Id)
            .OrderBy(a => a.MoodleCmId)
            .ToListAsync(ct);

        var actividadIds = actividades.Select(a => a.Id).ToList();
        var estudianteIds = estudiantes.Select(e => e.Id).ToHashSet();
        var calificaciones = await db.Calificaciones.AsNoTracking()
            .Where(c => actividadIds.Contains(c.ActividadId))
            .ToListAsync(ct);

        return new CalificacionesSincronizadas(
            Guid.NewGuid(),
            DateTime.UtcNow,
            new MateriaSnapshot(materia.Id, materia.Nombre, materia.DocenteEmail),
            estudiantes.Select(e => new EstudianteSnapshot(e.Id, e.Nombres, e.Apellidos)).ToList(),
            actividades.Select((a, i) => new ActividadSnapshot(a.Id, a.Nombre, a.Tipo.ToString(), a.Peso, i + 1)).ToList(),
            calificaciones.Where(c => estudianteIds.Contains(c.EstudianteId))
                .Select(c => new CalificacionSnapshot(c.ActividadId, c.EstudianteId, c.Nota, c.Estado.ToString())).ToList());
    }

    public async Task<MapeoActividadActualizado> ConstruirMapeoAsync(int materiaId, CancellationToken ct = default)
    {
        var materia = await db.Materias.AsNoTracking().FirstAsync(m => m.Id == materiaId, ct);
        var unidades = await db.Unidades.AsNoTracking().Where(u => u.MateriaId == materiaId).OrderBy(u => u.Orden).ToListAsync(ct);
        var unidadIds = unidades.Select(u => u.Id).ToList();
        var temas = await db.Temas.AsNoTracking().Where(t => unidadIds.Contains(t.UnidadId)).OrderBy(t => t.Orden).ToListAsync(ct);
        var temaIds = temas.Select(t => t.Id).ToList();
        var contenidos = await db.Contenidos.AsNoTracking().Where(c => temaIds.Contains(c.TemaId)).OrderBy(c => c.Orden).ToListAsync(ct);
        var mapeos = await db.Mapeos.AsNoTracking()
            .Where(m => m.Actividad!.MateriaId == materiaId)
            .Select(m => new MapeoSnapshot(m.ActividadId, m.ContenidoId))
            .ToListAsync(ct);

        return new MapeoActividadActualizado(
            Guid.NewGuid(),
            DateTime.UtcNow,
            new MateriaSnapshot(materia.Id, materia.Nombre, materia.DocenteEmail),
            unidades.Select(u => new UnidadSnapshot(u.Id, u.Nombre, u.Orden)).ToList(),
            temas.Select(t => new TemaSnapshot(t.Id, t.UnidadId, t.Nombre, t.Orden)).ToList(),
            contenidos.Select(c => new ContenidoSnapshot(c.Id, c.TemaId, c.Nombre, c.Orden)).ToList(),
            mapeos);
    }
}

/// <summary>Publica MapeoActividadActualizado sin que un fallo del bus afecte la operación del docente.</summary>
public class MapeoEventDispatcher(EventSnapshotBuilder builder, IEventPublisher events, ILogger<MapeoEventDispatcher> logger)
{
    public async Task<bool> PublicarAsync(int materiaId, CancellationToken ct = default)
    {
        try
        {
            var evento = await builder.ConstruirMapeoAsync(materiaId, ct);
            var publicado = await events.PublishAsync(EventNames.MapeoActividadActualizado, evento, ct);
            if (!publicado) logger.LogWarning("El mapeo de la materia {Materia} se guardó pero el evento no se publicó", materiaId);
            return publicado;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo armar o publicar el evento de mapeo de la materia {Materia}", materiaId);
            return false;
        }
    }
}
