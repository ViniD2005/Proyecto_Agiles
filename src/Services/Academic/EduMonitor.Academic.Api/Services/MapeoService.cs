using EduMonitor.Academic.Api.Contracts;
using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Domain;
using EduMonitor.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Services;

/// <summary>HU-09: relación N:M entre actividades y contenidos mínimos.</summary>
public class MapeoService(AcademicDbContext db, MapeoEventDispatcher dispatcher)
{
    public async Task<MapeoDto> ObtenerAsync(int materiaId, CancellationToken ct = default)
    {
        var actividades = await db.Actividades.AsNoTracking()
            .Where(a => a.MateriaId == materiaId)
            .OrderBy(a => a.MoodleCmId)
            .ToListAsync(ct);
        var relaciones = await CargarRelacionesAsync(materiaId, null, ct);

        var dto = actividades.Select(a => ArmarActividad(a, relaciones)).ToList();
        return new MapeoDto(materiaId, dto.Count(a => a.Pendiente), dto);
    }

    /// <summary>
    /// Reemplaza los contenidos de una actividad (uno o varios). Una lista vacía deja la actividad «pendiente de mapeo».
    /// Al confirmar se publica MapeoActividadActualizado (HU-09, criterio 3).
    /// </summary>
    public async Task<Resultado<AsignacionDto>> AsignarAsync(Actividad actividad, IReadOnlyCollection<int>? contenidoIds, CancellationToken ct = default)
    {
        var ids = (contenidoIds ?? Array.Empty<int>()).Distinct().ToList();

        if (ids.Count > 0)
        {
            var validos = await db.Contenidos
                .Where(c => ids.Contains(c.Id) && c.Tema!.Unidad!.MateriaId == actividad.MateriaId)
                .Select(c => c.Id).ToListAsync(ct);
            if (validos.Count != ids.Count)
                return Resultado<AsignacionDto>.Falla(CodigoResultado.Invalido, "Alguno de los contenidos no existe o no pertenece a esta materia.");
        }

        var existentes = await db.Mapeos.Where(m => m.ActividadId == actividad.Id).ToListAsync(ct);
        db.Mapeos.RemoveRange(existentes.Where(m => !ids.Contains(m.ContenidoId)));
        var yaAsignados = existentes.Select(m => m.ContenidoId).ToHashSet();
        foreach (var id in ids.Where(i => !yaAsignados.Contains(i)))
            db.Mapeos.Add(new ActividadContenido { ActividadId = actividad.Id, ContenidoId = id });
        await db.SaveChangesAsync(ct);

        var publicado = await dispatcher.PublicarAsync(actividad.MateriaId, ct);
        var relaciones = await CargarRelacionesAsync(actividad.MateriaId, actividad.Id, ct);
        return Resultado<AsignacionDto>.Ok(new AsignacionDto(ArmarActividad(actividad, relaciones), publicado));
    }

    private static ActividadMapeoDto ArmarActividad(Actividad a, List<Relacion> relaciones)
    {
        var lista = relaciones.Where(r => r.ActividadId == a.Id)
            .OrderBy(r => r.UnidadOrden).ThenBy(r => r.TemaOrden).ThenBy(r => r.ContenidoOrden)
            .Select(r => new RelacionContenidoDto(r.ContenidoId, r.Contenido, r.TemaId, r.Tema, r.UnidadId, r.Unidad))
            .ToList();
        return new ActividadMapeoDto(a.Id, a.Nombre, a.Tipo.ToString(), a.Peso, lista.Count == 0, lista);
    }

    private sealed record Relacion(int ActividadId, int ContenidoId, string Contenido, int ContenidoOrden,
        int TemaId, string Tema, int TemaOrden, int UnidadId, string Unidad, int UnidadOrden);

    private async Task<List<Relacion>> CargarRelacionesAsync(int materiaId, int? actividadId, CancellationToken ct)
    {
        var consulta =
            from m in db.Mapeos.AsNoTracking()
            join c in db.Contenidos.AsNoTracking() on m.ContenidoId equals c.Id
            join t in db.Temas.AsNoTracking() on c.TemaId equals t.Id
            join u in db.Unidades.AsNoTracking() on t.UnidadId equals u.Id
            where u.MateriaId == materiaId && (actividadId == null || m.ActividadId == actividadId)
            select new Relacion(m.ActividadId, c.Id, c.Nombre, c.Orden, t.Id, t.Nombre, t.Orden, u.Id, u.Nombre, u.Orden);
        return await consulta.ToListAsync(ct);
    }
}
