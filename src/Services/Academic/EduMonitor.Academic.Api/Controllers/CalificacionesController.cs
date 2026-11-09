using EduMonitor.Academic.Api.Contracts;
using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Controllers;

/// <summary>HU-07 · Calificaciones sincronizadas desde Moodle (matriz estudiantes × actividades).</summary>
[Route("api/materias/{materiaId:int}/calificaciones")]
public class CalificacionesController(AcademicDbContext db) : MateriaControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> Obtener(int materiaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        var (materia, error) = await MateriaAutorizadaAsync(materiaId, ct);
        if (error is not null) return error;

        (page, pageSize) = (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

        var actividades = await Db.Actividades.AsNoTracking()
            .Where(a => a.MateriaId == materiaId).OrderBy(a => a.MoodleCmId).ToListAsync(ct);

        var consulta = Db.Matriculas.AsNoTracking()
            .Where(m => m.MateriaId == materiaId && m.Activa)
            .Select(m => m.Estudiante!);
        var total = await consulta.CountAsync(ct);
        var pagina = await consulta.OrderBy(e => e.Apellidos).ThenBy(e => e.Nombres)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var estudianteIds = pagina.Select(e => e.Id).ToList();
        var actividadIds = actividades.Select(a => a.Id).ToList();
        var notas = await Db.Calificaciones.AsNoTracking()
            .Where(c => estudianteIds.Contains(c.EstudianteId) && actividadIds.Contains(c.ActividadId))
            .ToListAsync(ct);

        var filas = pagina.Select(e => new EstudianteNotasDto(
            e.Id,
            GradeStateClassifier.NombreVisible(e.Nombres, e.Apellidos, e.Email),
            notas.Where(n => n.EstudianteId == e.Id)
                 .ToDictionary(n => n.ActividadId, n => new NotaCeldaDto(n.Nota, n.Estado.ToString())))).ToList();

        return Ok(new CalificacionesDto(
            materiaId,
            materia!.UltimaSincronizacion,
            actividades.Select(a => new ActividadColumnaDto(a.Id, a.Nombre, a.Tipo.ToString(), a.Peso)).ToList(),
            new PagedResult<EstudianteNotasDto>(filas, page, pageSize, total)));
    }
}
