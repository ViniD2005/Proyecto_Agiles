using EduMonitor.Analytics.Api.Reportes;
using EduMonitor.Common;
using EduMonitor.Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Analytics.Api.Controllers;

/// <summary>MS-C · HU-16: reporte inteligente del curso (vista previa y PDF).</summary>
[ApiController]
[Authorize(Policy = Policies.SoloDocente)]
public class ReportesController(ReporteService servicio) : ControllerBase
{
    private string? Email => User.GetEmail();

    /// <summary>Genera un reporte nuevo con las cifras actuales y lo guarda; devuelve la vista previa.</summary>
    [HttpPost("api/materias/{materiaId:int}/reporte")]
    public async Task<IActionResult> Generar(int materiaId, CancellationToken ct) =>
        (await servicio.GenerarAsync(materiaId, Email, ct)).ToActionResult(this, r => StatusCode(StatusCodes.Status201Created, r));

    /// <summary>Último reporte generado de la materia (404 si aún no hay).</summary>
    [HttpGet("api/materias/{materiaId:int}/reporte")]
    public async Task<IActionResult> Ultimo(int materiaId, CancellationToken ct) =>
        (await servicio.UltimoAsync(materiaId, Email, ct)).ToActionResult(this);

    /// <summary>Descarga el PDF del reporte guardado (mismo contenido que la vista previa).</summary>
    [HttpGet("api/reportes/{reporteId:long}/pdf")]
    public async Task<IActionResult> Pdf(long reporteId, CancellationToken ct)
    {
        var r = await servicio.ObtenerPdfAsync(reporteId, Email, ct);
        return r.EsOk ? File(r.Valor.Bytes, "application/pdf", r.Valor.Nombre) : r.ToActionResult(this);
    }
}
