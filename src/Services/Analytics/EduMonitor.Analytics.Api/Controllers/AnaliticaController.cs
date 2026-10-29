using EduMonitor.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Analytics.Api.Controllers;

[ApiController]
[Route("api/analitica")]
public class AnaliticaController : ControllerBase
{
    /// <summary>Comprueba que el Gateway enruta y valida el JWT hacia MS-C (HU-03, criterio 3).</summary>
    [HttpGet("ping")]
    [Authorize(Policy = Policies.SoloDocente)]
    public IActionResult Ping() => Ok(new
    {
        servicio = "MS-C Analítica y Agente IA",
        usuario = User.GetEmail(),
        mensaje = "Indicadores, riesgo y agente IA se implementan en los Sprints 2 y 3."
    });
}
