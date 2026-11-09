using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Domain;
using EduMonitor.Academic.Api.Services;
using EduMonitor.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Controllers;

/// <summary>Valida que la materia exista y pertenezca al docente autenticado (RN-07) antes de cada operación.</summary>
[ApiController]
[Authorize(Policy = Policies.SoloDocente)]
public abstract class MateriaControllerBase(AcademicDbContext db) : ControllerBase
{
    /// <summary>Contexto de datos para los controladores derivados (evita capturar el parámetro dos veces).</summary>
    protected AcademicDbContext Db => db;

    protected string EmailDocente => User.GetEmail() ?? string.Empty;

    protected async Task<(Materia? Materia, IActionResult? Error)> MateriaAutorizadaAsync(int materiaId, CancellationToken ct)
    {
        var materia = await db.Materias.AsNoTracking().FirstOrDefaultAsync(m => m.Id == materiaId, ct);
        if (materia is null)
            return (null, Problem(detail: "La materia no existe.", statusCode: StatusCodes.Status404NotFound, title: "No encontrada"));
        if (!MateriaAccess.PuedeAcceder(materia, EmailDocente))
            return (null, Problem(detail: "No tienes permiso para esta materia.", statusCode: StatusCodes.Status403Forbidden, title: "Acceso denegado"));
        return (materia, null);
    }
}
