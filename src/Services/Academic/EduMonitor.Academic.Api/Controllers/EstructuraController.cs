using EduMonitor.Academic.Api.Contracts;
using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Services;
using EduMonitor.Common.Results;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Academic.Api.Controllers;

/// <summary>HU-08 · CRUD de unidades, temas y contenidos mínimos de la materia.</summary>
[Route("api/materias/{materiaId:int}/estructura")]
public class EstructuraController(AcademicDbContext db, EstructuraService servicio) : MateriaControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> Obtener(int materiaId, CancellationToken ct)
    {
        var (_, error) = await MateriaAutorizadaAsync(materiaId, ct);
        if (error is not null) return error;
        return Ok(await servicio.ObtenerAsync(materiaId, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Crear(int materiaId, [FromBody] CrearElementoRequest request, CancellationToken ct)
    {
        var (_, error) = await MateriaAutorizadaAsync(materiaId, ct);
        if (error is not null) return error;
        var r = await servicio.CrearAsync(materiaId, request.Nivel, request.PadreId, request.Nombre, ct);
        return r.ToActionResult(this, e => StatusCode(StatusCodes.Status201Created, e));
    }

    [HttpPut("{nivel}/{itemId:int}")]
    public async Task<IActionResult> Renombrar(int materiaId, string nivel, int itemId, [FromBody] RenombrarRequest request, CancellationToken ct)
    {
        var (_, error) = await MateriaAutorizadaAsync(materiaId, ct);
        if (error is not null) return error;
        var r = await servicio.RenombrarAsync(materiaId, nivel, itemId, request.Nombre, ct);
        return r.ToActionResult(this);
    }

    /// <summary>Si el elemento está asignado a actividades responde 409 y pide confirmar=true.</summary>
    [HttpDelete("{nivel}/{itemId:int}")]
    public async Task<IActionResult> Eliminar(int materiaId, string nivel, int itemId, [FromQuery] bool confirmar = false, CancellationToken ct = default)
    {
        var (_, error) = await MateriaAutorizadaAsync(materiaId, ct);
        if (error is not null) return error;
        var r = await servicio.EliminarAsync(materiaId, nivel, itemId, confirmar, ct);
        return r.ToActionResult(this, _ => NoContent());
    }

    /// <summary>Carga la estructura completa del sílabo (solo si la materia aún no tiene estructura).</summary>
    [HttpPost("importar")]
    public async Task<IActionResult> Importar(int materiaId, [FromBody] ImportarEstructuraRequest request, CancellationToken ct)
    {
        var (_, error) = await MateriaAutorizadaAsync(materiaId, ct);
        if (error is not null) return error;
        var r = await servicio.ImportarAsync(materiaId, request, ct);
        return r.ToActionResult(this, e => StatusCode(StatusCodes.Status201Created, e));
    }
}
