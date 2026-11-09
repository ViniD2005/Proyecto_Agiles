using EduMonitor.Academic.Api.Contracts;
using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Services;
using EduMonitor.Common.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Controllers;

/// <summary>HU-09 · Mapeo de actividades con los contenidos mínimos que evalúan.</summary>
public class MapeoController(AcademicDbContext db, MapeoService servicio) : MateriaControllerBase(db)
{
    /// <summary>Actividades con sus contenidos; las que no tienen ninguno se marcan como pendientes.</summary>
    [HttpGet("api/materias/{materiaId:int}/mapeo")]
    public async Task<IActionResult> Obtener(int materiaId, CancellationToken ct)
    {
        var (_, error) = await MateriaAutorizadaAsync(materiaId, ct);
        if (error is not null) return error;
        return Ok(await servicio.ObtenerAsync(materiaId, ct));
    }

    /// <summary>Asigna uno o varios contenidos a la actividad (reemplaza los anteriores).</summary>
    [HttpPut("api/actividades/{actividadId:int}/contenidos")]
    public async Task<IActionResult> Asignar(int actividadId, [FromBody] AsignarContenidosRequest request, CancellationToken ct)
    {
        var actividad = await Db.Actividades.AsNoTracking().FirstOrDefaultAsync(a => a.Id == actividadId, ct);
        if (actividad is null)
            return Problem(detail: "La actividad no existe.", statusCode: StatusCodes.Status404NotFound, title: "No encontrada");

        var (_, error) = await MateriaAutorizadaAsync(actividad.MateriaId, ct);
        if (error is not null) return error;

        var r = await servicio.AsignarAsync(actividad, request.ContenidoIds, ct);
        return r.ToActionResult(this);
    }
}
