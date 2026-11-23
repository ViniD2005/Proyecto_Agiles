using EduMonitor.Common;
using EduMonitor.Identity.Api.Contracts;
using EduMonitor.Identity.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Identity.Api.Controllers;

/// <summary>Bitácora de auditoría (RF-12, HU-17). Solo el administrador técnico puede consultarla (RN-07 / RNF-01).</summary>
[ApiController]
[Route("api/auditoria")]
public class AuditoriaController(AuditoriaService auditoria) : ControllerBase
{
    /// <summary>Lista paginada con filtros por acción, origen (servicio), usuario y rango de fechas (UTC).</summary>
    [HttpGet]
    [Authorize(Policy = Policies.SoloAdministrador)]
    public async Task<ActionResult<PagedResult<AuditoriaDto>>> Listar(
        [FromQuery] string? accion, [FromQuery] string? origen, [FromQuery] string? usuario,
        [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        await auditoria.ConsultarAsync(new AuditoriaFiltro(accion, origen, usuario, desde, hasta, page, pageSize), ct);

    /// <summary>Acciones registradas (para el filtro de la pantalla).</summary>
    [HttpGet("acciones")]
    [Authorize(Policy = Policies.SoloAdministrador)]
    public async Task<ActionResult<List<string>>> Acciones(CancellationToken ct) => await auditoria.AccionesAsync(ct);

    /// <summary>Permite a otros servicios registrar eventos de auditoría con el token del usuario.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Autenticado)]
    public async Task<IActionResult> Registrar([FromBody] RegistrarAuditoriaRequest req, CancellationToken ct)
    {
        var ok = await auditoria.RegistrarAsync(User.GetEmail(), req, ct);
        return ok ? NoContent() : BadRequest(new { mensaje = "Acción y servicio son obligatorios." });
    }
}
