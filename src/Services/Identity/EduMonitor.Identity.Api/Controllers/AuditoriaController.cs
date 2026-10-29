using EduMonitor.Common;
using EduMonitor.Identity.Api.Contracts;
using EduMonitor.Identity.Api.Data;
using EduMonitor.Identity.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Identity.Api.Controllers;

/// <summary>Bitácora de auditoría (RF-12). La vista completa se termina en el Sprint 3 (HU-17).</summary>
[ApiController]
[Route("api/auditoria")]
public class AuditoriaController(IdentityDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.SoloAdministrador)]
    public async Task<ActionResult<PagedResult<AuditoriaDto>>> Listar([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Auditoria.AsNoTracking().OrderByDescending(a => a.Fecha);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditoriaDto(a.Id, a.Fecha, a.UsuarioEmail, a.Accion, a.Servicio, a.Detalle))
            .ToListAsync(ct);
        return new PagedResult<AuditoriaDto>(items, page, pageSize, total);
    }

    /// <summary>Permite a otros servicios registrar eventos de auditoría con el token del usuario.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Autenticado)]
    public async Task<IActionResult> Registrar([FromBody] RegistrarAuditoriaRequest req, CancellationToken ct)
    {
        db.Auditoria.Add(new RegistroAuditoria
        {
            UsuarioEmail = User.GetEmail(),
            Accion = req.Accion[..Math.Min(req.Accion.Length, 60)],
            Servicio = req.Servicio[..Math.Min(req.Servicio.Length, 20)],
            Detalle = req.Detalle is null ? null : req.Detalle[..Math.Min(req.Detalle.Length, 1000)]
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
