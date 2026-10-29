using EduMonitor.Academic.Api.Contracts;
using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Domain;
using EduMonitor.Academic.Api.Services;
using EduMonitor.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Controllers;

[ApiController]
[Route("api/materias")]
[Authorize(Policy = Policies.SoloDocente)]
public class MateriasController(AcademicDbContext db, ISyncService sync) : ControllerBase
{
    private string Email => User.GetEmail() ?? string.Empty;

    /// <summary>Materias del docente autenticado (HU-05).</summary>
    [HttpGet]
    public async Task<ActionResult<List<MateriaDto>>> MisMaterias(CancellationToken ct) =>
        await db.Materias.AsNoTracking()
            .Where(m => m.DocenteEmail == Email)
            .OrderBy(m => m.Nombre)
            .Select(m => new MateriaDto(m.Id, m.MoodleCourseId, m.Nombre, m.NombreCorto, m.TotalEstudiantes,
                m.TotalActividades, m.UltimaSincronizacion, m.OrigenSincronizacion))
            .ToListAsync(ct);

    /// <summary>Sincroniza desde Moodle las materias asignadas al docente (y su detalle).</summary>
    [HttpPost("sincronizar")]
    public async Task<ActionResult<ResultadoSincronizacion>> SincronizarMaterias(CancellationToken ct)
    {
        var r = await sync.SincronizarMateriasAsync(Email, ct);
        return r.Exitosa ? Ok(r) : StatusCode(StatusCodes.Status502BadGateway, r);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MateriaDto>> Obtener(int id, CancellationToken ct)
    {
        var m = await db.Materias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return NotFound();
        if (!MateriaAccess.PuedeAcceder(m, Email)) return Forbid();
        return new MateriaDto(m.Id, m.MoodleCourseId, m.Nombre, m.NombreCorto, m.TotalEstudiantes,
            m.TotalActividades, m.UltimaSincronizacion, m.OrigenSincronizacion);
    }

    /// <summary>Sincroniza estudiantes y actividades de una materia (HU-06).</summary>
    [HttpPost("{id:int}/sincronizar")]
    public async Task<ActionResult<ResultadoSincronizacion>> SincronizarMateria(int id, CancellationToken ct)
    {
        var m = await db.Materias.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return NotFound();
        if (!MateriaAccess.PuedeAcceder(m, Email)) return Forbid();
        var r = await sync.SincronizarDetalleAsync(m, Email, ct);
        return r.Exitosa ? Ok(r) : StatusCode(StatusCodes.Status502BadGateway, r);
    }

    /// <summary>Estudiantes de la materia, paginados y con búsqueda por nombre (HU-06).</summary>
    [HttpGet("{id:int}/estudiantes")]
    public async Task<ActionResult<PagedResult<EstudianteDto>>> Estudiantes(int id, [FromQuery] string? buscar,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        var m = await db.Materias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return NotFound();
        if (!MateriaAccess.PuedeAcceder(m, Email)) return Forbid();

        (page, pageSize) = (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
        var q = db.Matriculas.AsNoTracking().Where(x => x.MateriaId == id && x.Activa).Select(x => x.Estudiante!);
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim();
            q = q.Where(e => e.Nombres.Contains(b) || e.Apellidos.Contains(b) || (e.Email != null && e.Email.Contains(b)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(e => e.Apellidos).ThenBy(e => e.Nombres)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new EstudianteDto(e.Id, e.MoodleUserId, e.Nombres, e.Apellidos, e.Email, "Activo"))
            .ToListAsync(ct);
        return new PagedResult<EstudianteDto>(items, page, pageSize, total);
    }

    /// <summary>Actividades de la materia clasificadas por tipo y con su peso (HU-06).</summary>
    [HttpGet("{id:int}/actividades")]
    public async Task<ActionResult<PagedResult<ActividadDto>>> Actividades(int id, [FromQuery] TipoActividad? tipo,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var m = await db.Materias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return NotFound();
        if (!MateriaAccess.PuedeAcceder(m, Email)) return Forbid();

        (page, pageSize) = (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
        var q = db.Actividades.AsNoTracking().Where(a => a.MateriaId == id);
        if (tipo.HasValue) q = q.Where(a => a.Tipo == tipo.Value);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(a => a.Tipo).ThenBy(a => a.Nombre)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new ActividadDto(a.Id, a.MoodleCmId, a.Nombre, a.Modulo, a.Tipo.ToString(), a.Peso, a.Seccion))
            .ToListAsync(ct);
        return new PagedResult<ActividadDto>(items, page, pageSize, total);
    }

    /// <summary>Historial de sincronizaciones de la materia (fecha, origen, resultado y errores).</summary>
    [HttpGet("{id:int}/sincronizaciones")]
    public async Task<ActionResult<List<SincronizacionDto>>> Sincronizaciones(int id, CancellationToken ct)
    {
        var m = await db.Materias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return NotFound();
        if (!MateriaAccess.PuedeAcceder(m, Email)) return Forbid();
        return await db.Sincronizaciones.AsNoTracking().Where(s => s.MateriaId == id)
            .OrderByDescending(s => s.Fecha).Take(20)
            .Select(s => new SincronizacionDto(s.Id, s.Fecha, s.Tipo, s.Origen, s.Exitosa, s.Mensaje))
            .ToListAsync(ct);
    }
}
