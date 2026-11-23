using EduMonitor.Common.Auditoria;
using EduMonitor.Identity.Api.Contracts;
using EduMonitor.Identity.Api.Data;
using EduMonitor.Identity.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Identity.Api.Services;

public record AuditoriaFiltro(string? Accion, string? Servicio, string? Usuario, DateTime? Desde, DateTime? Hasta, int Page = 1, int PageSize = 20);

/// <summary>HU-17: consulta con filtros y registro seguro (sin tokens ni contraseñas) de la bitácora de auditoría.</summary>
public class AuditoriaService(IdentityDbContext db)
{
    public const int MaxAccion = 60, MaxServicio = 20, MaxDetalle = 1000;

    public async Task<PagedResult<AuditoriaDto>> ConsultarAsync(AuditoriaFiltro f, CancellationToken ct = default)
    {
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        IQueryable<RegistroAuditoria> q = db.Auditoria.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.Accion)) { var a = f.Accion.Trim(); q = q.Where(x => x.Accion == a); }
        if (!string.IsNullOrWhiteSpace(f.Servicio)) { var s = f.Servicio.Trim(); q = q.Where(x => x.Servicio == s); }
        if (!string.IsNullOrWhiteSpace(f.Usuario))
        {
            var u = f.Usuario.Trim().ToLowerInvariant();
            q = q.Where(x => x.UsuarioEmail != null && x.UsuarioEmail.ToLower().Contains(u));
        }
        if (f.Desde is not null) { var d = f.Desde.Value.Date; q = q.Where(x => x.Fecha >= d); }
        if (f.Hasta is not null) { var h = f.Hasta.Value.Date.AddDays(1); q = q.Where(x => x.Fecha < h); }

        var total = await q.CountAsync(ct);
        var filas = await q.OrderByDescending(x => x.Fecha).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var items = filas.Select(x => new AuditoriaDto(x.Id, x.Fecha, x.UsuarioEmail, x.Accion, x.Servicio,
            AuditSanitizer.Limpiar(x.Detalle, MaxDetalle))).ToList();
        return new PagedResult<AuditoriaDto>(items, page, pageSize, total);
    }

    public async Task<List<string>> AccionesAsync(CancellationToken ct = default) =>
        await db.Auditoria.AsNoTracking().Select(x => x.Accion).Distinct().OrderBy(x => x).ToListAsync(ct);

    /// <summary>Guarda un evento recibido de otro servicio. Devuelve false si la solicitud no es válida.</summary>
    public async Task<bool> RegistrarAsync(string? usuarioEmail, RegistrarAuditoriaRequest? req, CancellationToken ct = default)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Accion) || string.IsNullOrWhiteSpace(req.Servicio)) return false;
        db.Auditoria.Add(new RegistroAuditoria
        {
            UsuarioEmail = usuarioEmail,
            Accion = Cortar(req.Accion.Trim().ToUpperInvariant(), MaxAccion),
            Servicio = Cortar(req.Servicio.Trim(), MaxServicio),
            Detalle = AuditSanitizer.Limpiar(req.Detalle, MaxDetalle)
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string Cortar(string t, int max) => t.Length <= max ? t : t[..max];
}
