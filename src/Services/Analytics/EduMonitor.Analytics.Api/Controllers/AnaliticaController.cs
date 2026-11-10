using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Services;
using EduMonitor.Common;
using EduMonitor.Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Analytics.Api.Controllers;

/// <summary>
/// MS-C · HU-10 indicadores, HU-11 riesgo y alertas, HU-12 mapa de calor y ficha del estudiante.
/// Cada consulta valida que la materia pertenezca al docente autenticado (RN-07).
/// </summary>
[ApiController]
[Authorize(Policy = Policies.SoloDocente)]
public class AnaliticaController(AnaliticaService servicio) : ControllerBase
{
    private string? Email => User.GetEmail();

    /// <summary>Comprueba que el Gateway enruta y valida el JWT hacia MS-C (HU-03, criterio 3).</summary>
    [HttpGet("api/analitica/ping")]
    public IActionResult Ping() => Ok(new
    {
        servicio = "MS-C Analítica y Agente IA",
        usuario = User.GetEmail(),
        mensaje = "Indicadores, riesgo y mapa de calor disponibles (Sprint 2). El agente de IA llega en el Sprint 3."
    });

    [HttpGet("api/materias/{materiaId:int}/indicadores")]
    public async Task<IActionResult> Indicadores(int materiaId, CancellationToken ct) =>
        (await servicio.ObtenerIndicadoresAsync(materiaId, Email, ct)).ToActionResult(this);

    [HttpGet("api/materias/{materiaId:int}/riesgo")]
    public async Task<IActionResult> Riesgo(int materiaId, CancellationToken ct) =>
        (await servicio.ObtenerRiesgoAsync(materiaId, Email, ct)).ToActionResult(this);

    [HttpGet("api/materias/{materiaId:int}/desempeno-por-tema")]
    public async Task<IActionResult> DesempenoPorTema(int materiaId, CancellationToken ct) =>
        (await servicio.ObtenerMapaCalorAsync(materiaId, Email, ct)).ToActionResult(this);

    [HttpGet("api/materias/{materiaId:int}/fichas/{estudianteId:int}")]
    public async Task<IActionResult> Ficha(int materiaId, int estudianteId, CancellationToken ct) =>
        (await servicio.ObtenerFichaAsync(materiaId, estudianteId, Email, ct)).ToActionResult(this);

    /// <summary>Configura los umbrales de riesgo de una materia y recalcula la clasificación (HU-11).</summary>
    [HttpPut("api/umbrales")]
    public async Task<IActionResult> ActualizarUmbrales([FromBody] ActualizarUmbralesRequest request, CancellationToken ct) =>
        (await servicio.ActualizarUmbralesAsync(request, Email, ct)).ToActionResult(this);
}
