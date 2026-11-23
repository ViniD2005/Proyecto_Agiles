using EduMonitor.Academic.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Controllers;

/// <summary>Datos mínimos de contacto de un estudiante (HU-20). Solo el dueño de la materia puede pedirlos (RN-07).</summary>
public record ContactoEstudianteDto(int EstudianteId, string Nombre, string? Email);

[Route("api/materias/{materiaId:int}/estudiantes/{estudianteId:int}/contacto")]
public class ContactoController(AcademicDbContext db) : MateriaControllerBase(db)
{
    /// <summary>
    /// MS-C lo consulta justo antes de enviar una recomendación aprobada. El correo no se replica en DB_Analitica
    /// (RNF-02: datos mínimos), por eso se pide a MS-B en el momento del envío.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Obtener(int materiaId, int estudianteId, CancellationToken ct)
    {
        var (_, error) = await MateriaAutorizadaAsync(materiaId, ct);
        if (error is not null) return error;

        var est = await Db.Matriculas.AsNoTracking()
            .Where(m => m.MateriaId == materiaId && m.EstudianteId == estudianteId && m.Activa)
            .Select(m => m.Estudiante!)
            .FirstOrDefaultAsync(ct);
        if (est is null)
            return Problem(detail: "El estudiante no pertenece a esta materia.", statusCode: StatusCodes.Status404NotFound, title: "No encontrado");

        return Ok(new ContactoEstudianteDto(est.Id, $"{est.Nombres} {est.Apellidos}".Trim(), est.Email));
    }
}
