using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace EduMonitor.Common.Auditoria;

/// <summary>Registra eventos en la bitácora central (MS-A). Nunca debe romper la acción del usuario.</summary>
public interface IAuditClient
{
    Task RegistrarAsync(string accion, string servicio, string? detalle, CancellationToken ct = default);
}

/// <summary>Sin efecto: para pruebas o cuando no hay bitácora disponible.</summary>
public sealed class NullAuditClient : IAuditClient
{
    public Task RegistrarAsync(string accion, string servicio, string? detalle, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Envía el evento a POST /api/auditoria de MS-A reenviando el token del usuario actual: así MS-A registra
/// quién hizo la acción (HU-17, criterio 1). Si MS-A no responde, solo se deja una advertencia en el log.
/// </summary>
public sealed class HttpAuditClient(HttpClient http, IHttpContextAccessor accessor, ILogger<HttpAuditClient> logger) : IAuditClient
{
    public async Task RegistrarAsync(string accion, string servicio, string? detalle, CancellationToken ct = default)
    {
        try
        {
            var autorizacion = accessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();
            if (string.IsNullOrWhiteSpace(autorizacion))
            {
                logger.LogDebug("Auditoría omitida ({Accion}): no hay token del usuario en el contexto.", accion);
                return;
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, "api/auditoria")
            {
                Content = JsonContent.Create(new { accion, servicio, detalle = AuditSanitizer.Limpiar(detalle) })
            };
            req.Headers.TryAddWithoutValidation(HeaderNames.Authorization, autorizacion);

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(4));
            using var resp = await http.SendAsync(req, limite.Token);
            if (!resp.IsSuccessStatusCode)
                logger.LogWarning("MS-A rechazó el registro de auditoría {Accion}: HTTP {Codigo}", accion, (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo registrar la auditoría {Accion}.", accion);
        }
    }
}

public static class AuditServiceCollectionExtensions
{
    /// <summary>Registra IAuditClient. Config: Audit:IdentityUrl (por defecto http://localhost:5001/).</summary>
    public static IServiceCollection AddEduMonitorAudit(this IServiceCollection services, IConfiguration config)
    {
        var url = config["Audit:IdentityUrl"];
        if (string.IsNullOrWhiteSpace(url)) url = "http://localhost:5001/";
        services.AddHttpContextAccessor();
        services.AddHttpClient<IAuditClient, HttpAuditClient>(h =>
        {
            h.BaseAddress = new Uri(url.TrimEnd('/') + "/");
            h.Timeout = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}

/// <summary>
/// Registra en la bitácora la acción de un endpoint cuando termina (HU-17, criterio 1).
/// Uso: [AuditarAccion("SINCRONIZACION_MATERIA", "MS-B")]. No incluye el cuerpo ni la cadena de consulta.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AuditarAccionAttribute(string accion, string servicio) : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var ejecutado = await next();
        try
        {
            var audit = context.HttpContext.RequestServices.GetService<IAuditClient>();
            if (audit is null) return;

            var estado = ejecutado.Exception is not null && !ejecutado.ExceptionHandled ? 500 : EstadoDe(ejecutado.Result);
            var materia = context.RouteData.Values.TryGetValue("materiaId", out var m) ? m
                : context.RouteData.Values.TryGetValue("id", out var i) ? i : null;
            var detalle = $"{(estado is >= 200 and < 300 ? "OK" : "FALLO")} · HTTP {estado} · {context.HttpContext.Request.Method} {context.HttpContext.Request.Path}"
                          + (materia is null ? string.Empty : $" · materia {materia}");
            await audit.RegistrarAsync(accion, servicio, detalle, context.HttpContext.RequestAborted);
        }
        catch
        {
            // la auditoría nunca debe romper la respuesta al usuario
        }
    }

    internal static int EstadoDe(IActionResult? resultado) => resultado switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        ForbidResult => 403,
        ChallengeResult => 401,
        ContentResult c => c.StatusCode ?? 200,
        JsonResult j => j.StatusCode ?? 200,
        _ => 200
    };
}
