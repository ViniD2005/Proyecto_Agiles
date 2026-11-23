using EduMonitor.Analytics.Api.Silabo;
using EduMonitor.Common;
using EduMonitor.Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Analytics.Api.Controllers;

/// <summary>MS-C · HU-21 (opcional): propuesta de estructura temática a partir del sílabo en PDF. No guarda nada.</summary>
[ApiController]
[Authorize(Policy = Policies.SoloDocente)]
public class SilaboController(SilaboService servicio) : ControllerBase
{
    /// <summary>
    /// Recibe el PDF (multipart, campo «archivo», máx. 5 MB) y devuelve la propuesta. Para guardarla, el docente la corrige y
    /// la confirma con POST /api/materias/{id}/estructura/importar (MS-B).
    /// </summary>
    [HttpPost("api/materias/{materiaId:int}/silabo/propuesta")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Proponer(int materiaId, IFormFile? archivo, CancellationToken ct)
    {
        if (archivo is null) return Problem(detail: "Adjunta el sílabo en PDF (campo «archivo»).", statusCode: StatusCodes.Status400BadRequest, title: "Solicitud inválida");
        await using var flujo = archivo.OpenReadStream();
        return (await servicio.ProponerAsync(materiaId, User.GetEmail(), flujo, archivo.Length, ct)).ToActionResult(this);
    }
}
