using EduMonitor.Analytics.Api.Agente;
using EduMonitor.Analytics.Api.Agente.Herramientas;
using EduMonitor.Analytics.Api.Agente.Llm;
using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Common;
using EduMonitor.Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Analytics.Api.Controllers;

/// <summary>MS-C · HU-13 y HU-14: chat del docente con el agente. Cada consulta valida el curso (RN-07).</summary>
[ApiController]
[Authorize(Policy = Policies.SoloDocente)]
public class AgenteController(AgenteOrquestador orquestador, ILlmProvider llm, RegistroHerramientas herramientas) : ControllerBase
{
    /// <summary>Consulta en lenguaje natural. 403 si la materia no es del docente; la respuesta separa hechos, inferencias y recomendaciones.</summary>
    [HttpPost("api/agente/chat")]
    public async Task<IActionResult> Chat([FromBody] ConsultaAgenteRequest request, CancellationToken ct) =>
        (await orquestador.ResponderAsync(User.GetEmail(), request, ct)).ToActionResult(this);

    /// <summary>Proveedor y modelo configurados, si están disponibles y qué herramientas tiene el agente.</summary>
    [HttpGet("api/agente/estado")]
    public async Task<ActionResult<EstadoAgenteDto>> Estado(CancellationToken ct)
    {
        var estado = await llm.ConsultarEstadoAsync(ct);
        return new EstadoAgenteDto(llm.Nombre, llm.Modelo, estado.Disponible, estado.Detalle,
            herramientas.Todas.Select(h => h.Nombre).OrderBy(n => n).ToList());
    }
}
