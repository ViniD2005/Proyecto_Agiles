using EduMonitor.Academic.Api.Contracts;
using EduMonitor.Academic.Api.Moodle;
using EduMonitor.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Academic.Api.Controllers;

[ApiController]
[Route("api/moodle")]
public class MoodleController(IMoodleClient moodle) : ControllerBase
{
    /// <summary>Verifica la conectividad con Moodle (core_webservice_get_site_info) - HU-02 criterio 1.</summary>
    [HttpGet("estado")]
    [Authorize(Policy = Policies.Autenticado)]
    public async Task<ActionResult<EstadoMoodleDto>> Estado(CancellationToken ct)
    {
        try
        {
            var info = await moodle.GetSiteInfoAsync(ct);
            return new EstadoMoodleDto(true, moodle.BaseUrl, info.SiteName, info.Release, null);
        }
        catch (Exception ex) when (ex is MoodleException or HttpRequestException or TaskCanceledException)
        {
            return new EstadoMoodleDto(false, moodle.BaseUrl, null, null, ex.Message);
        }
    }
}
