using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Recomendaciones;
using EduMonitor.Common;
using EduMonitor.Common.Auditoria;
using EduMonitor.Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Analytics.Api.Controllers;

/// <summary>MS-C · HU-15 recomendaciones por estudiante (editar y aprobar) y HU-20 envío manual por correo.</summary>
[ApiController]
[Authorize(Policy = Policies.SoloDocente)]
[Route("api/materias/{materiaId:int}/recomendaciones")]
public class RecomendacionesController(RecomendacionService servicio, EnvioRecomendacionService envio) : ControllerBase
{
    private string? Email => User.GetEmail();

    [HttpGet]
    public async Task<IActionResult> Listar(int materiaId, CancellationToken ct) =>
        (await servicio.ListarAsync(materiaId, Email, ct)).ToActionResult(this);

    [HttpGet("{estudianteId:int}")]
    public async Task<IActionResult> Obtener(int materiaId, int estudianteId, CancellationToken ct) =>
        (await servicio.ObtenerAsync(materiaId, estudianteId, Email, ct)).ToActionResult(this);

    /// <summary>Genera (o regenera, si aún es borrador) la recomendación del estudiante.</summary>
    [HttpPost("{estudianteId:int}/generar")]
    public async Task<IActionResult> Generar(int materiaId, int estudianteId, CancellationToken ct) =>
        (await servicio.GenerarAsync(materiaId, estudianteId, Email, ct)).ToActionResult(this);

    /// <summary>Edita la recomendación; vuelve a borrador y exige una nueva aprobación.</summary>
    [HttpPut("{estudianteId:int}")]
    public async Task<IActionResult> Editar(int materiaId, int estudianteId, [FromBody] EditarRecomendacionRequest request, CancellationToken ct) =>
        (await servicio.EditarAsync(materiaId, estudianteId, request, Email, ct)).ToActionResult(this);

    [HttpPost("{estudianteId:int}/aprobar")]
    public async Task<IActionResult> Aprobar(int materiaId, int estudianteId, CancellationToken ct) =>
        (await servicio.AprobarAsync(materiaId, estudianteId, Email, ct)).ToActionResult(this);

    /// <summary>
    /// HU-20 (opcional): envía por correo la recomendación APROBADA. Solo ocurre cuando el docente lo pide
    /// con esta llamada; nunca se dispara automáticamente. 409 si no está aprobada.
    /// </summary>
    [HttpPost("{estudianteId:int}/enviar")]
    public async Task<IActionResult> Enviar(int materiaId, int estudianteId, CancellationToken ct)
    {
        try
        {
            return (await envio.EnviarAsync(materiaId, estudianteId, Email, ct)).ToActionResult(this);
        }
        catch (CorreoNoDisponibleException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Correo no disponible");
        }
    }
}
