using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Domain;
using EduMonitor.Common.Messaging;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Analytics.Api.Services;

/// <summary>
/// Consume los eventos de MS-B y reemplaza la copia local de la materia. Es idempotente:
/// repetir un evento deja el mismo resultado y un evento más antiguo que el ya aplicado se ignora.
/// </summary>
public class SnapshotIngestor(AnalyticsDbContext db, RecalculoService recalculo, ILogger<SnapshotIngestor> logger)
{
    public async Task IngerirCalificacionesAsync(CalificacionesSincronizadas e, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.ChangeTracker.Clear();
        var materia = await UpsertMateriaAsync(e.Materia, ct);
        if (materia.UltimasCalificacionesUtc is { } ultima && ultima >= e.OcurrioEnUtc)
        {
            logger.LogInformation("Evento de calificaciones {Id} ignorado: hay uno más reciente", e.EventId);
            return;
        }
        var id = e.Materia.Id;
        materia.UltimasCalificacionesUtc = e.OcurrioEnUtc;
        await db.SaveChangesAsync(ct);

        await db.Estudiantes.Where(x => x.MateriaId == id).ExecuteDeleteAsync(ct);
        await db.Actividades.Where(x => x.MateriaId == id).ExecuteDeleteAsync(ct);
        await db.Calificaciones.Where(x => x.MateriaId == id).ExecuteDeleteAsync(ct);
        db.ChangeTracker.Clear();   // los borrados masivos no actualizan las entidades ya rastreadas

        db.Estudiantes.AddRange(e.Estudiantes.Select(x => new EstudianteAnalitico
            { MateriaId = id, EstudianteId = x.Id, Nombres = x.Nombres, Apellidos = x.Apellidos }));
        db.Actividades.AddRange(e.Actividades.Select(x => new ActividadAnalitica
            { MateriaId = id, ActividadId = x.Id, Nombre = x.Nombre, Tipo = x.Tipo, Peso = x.Peso, Orden = x.Orden }));
        db.Calificaciones.AddRange(e.Calificaciones.Select(x => new CalificacionAnalitica
            { MateriaId = id, ActividadId = x.ActividadId, EstudianteId = x.EstudianteId, Nota = x.Nota, Estado = x.Estado }));
        await db.SaveChangesAsync(ct);

        await recalculo.RecalcularAsync(id, ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Calificaciones de la materia {Materia} aplicadas ({Notas} notas)", id, e.Calificaciones.Count);
    }

    public async Task IngerirMapeoAsync(MapeoActividadActualizado e, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.ChangeTracker.Clear();
        var materia = await UpsertMateriaAsync(e.Materia, ct);
        if (materia.UltimoMapeoUtc is { } ultimo && ultimo >= e.OcurrioEnUtc)
        {
            logger.LogInformation("Evento de mapeo {Id} ignorado: hay uno más reciente", e.EventId);
            return;
        }
        var id = e.Materia.Id;
        materia.UltimoMapeoUtc = e.OcurrioEnUtc;
        await db.SaveChangesAsync(ct);

        await db.Unidades.Where(x => x.MateriaId == id).ExecuteDeleteAsync(ct);
        await db.Temas.Where(x => x.MateriaId == id).ExecuteDeleteAsync(ct);
        await db.Contenidos.Where(x => x.MateriaId == id).ExecuteDeleteAsync(ct);
        await db.Mapeos.Where(x => x.MateriaId == id).ExecuteDeleteAsync(ct);
        db.ChangeTracker.Clear();

        db.Unidades.AddRange(e.Unidades.Select(x => new UnidadAnalitica { MateriaId = id, UnidadId = x.Id, Nombre = x.Nombre, Orden = x.Orden }));
        db.Temas.AddRange(e.Temas.Select(x => new TemaAnalitico { MateriaId = id, TemaId = x.Id, UnidadId = x.UnidadId, Nombre = x.Nombre, Orden = x.Orden }));
        db.Contenidos.AddRange(e.Contenidos.Select(x => new ContenidoAnalitico { MateriaId = id, ContenidoId = x.Id, TemaId = x.TemaId, Nombre = x.Nombre, Orden = x.Orden }));
        db.Mapeos.AddRange(e.Mapeo.Select(x => new MapeoAnalitico { MateriaId = id, ActividadId = x.ActividadId, ContenidoId = x.ContenidoId }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Estructura y mapeo de la materia {Materia} aplicados ({Mapeos} relaciones)", id, e.Mapeo.Count);
    }

    private async Task<MateriaAnalitica> UpsertMateriaAsync(MateriaSnapshot s, CancellationToken ct)
    {
        var materia = await db.Materias.FirstOrDefaultAsync(m => m.Id == s.Id, ct);
        if (materia is null)
        {
            materia = new MateriaAnalitica { Id = s.Id };
            db.Materias.Add(materia);
        }
        materia.Nombre = s.Nombre;
        materia.DocenteEmail = s.DocenteEmail.Trim().ToLowerInvariant();
        await db.SaveChangesAsync(ct);
        return materia;
    }
}
