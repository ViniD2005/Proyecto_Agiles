using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Domain;
using EduMonitor.Common.Auditoria;
using EduMonitor.Common.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace EduMonitor.Analytics.Api.Recomendaciones;

public record ContactoEstudiante(string Nombre, string? Email);
public record MensajeCorreo(string Para, string NombreDestino, string Asunto, string Cuerpo);

/// <summary>No se pudo entregar el correo (SMTP sin configurar, rechazado o sin conexión).</summary>
public class CorreoNoDisponibleException(string mensaje, Exception? inner = null) : Exception(mensaje, inner);

public interface IEnviadorCorreo
{
    Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct = default);
}

/// <summary>Correo del estudiante, consultado a MS-B en el momento del envío (no se replica en DB_Analitica: RNF-02).</summary>
public interface IContactoEstudiante
{
    Task<(ContactoEstudiante? Contacto, string? Error)> ObtenerAsync(int materiaId, int estudianteId, CancellationToken ct = default);
}

public sealed class SmtpOptions
{
    public const string Section = "Smtp";
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@edumonitor.local";
    public string FromName { get; set; } = "EduMonitor AI";
    /// <summary>true = STARTTLS (587). Para Mailpit local (puerto 1025) use false.</summary>
    public bool UseStartTls { get; set; } = true;
}

public sealed class HttpContactoEstudiante(HttpClient http, Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) : IContactoEstudiante
{
    public async Task<(ContactoEstudiante? Contacto, string? Error)> ObtenerAsync(int materiaId, int estudianteId, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"api/materias/{materiaId}/estudiantes/{estudianteId}/contacto");
            var token = accessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();
            if (!string.IsNullOrWhiteSpace(token)) req.Headers.TryAddWithoutValidation(HeaderNames.Authorization, token);
            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return (null, "El estudiante no pertenece a esta materia.");
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden) return (null, "No tienes permiso sobre esta materia.");
            if (!resp.IsSuccessStatusCode) return (null, $"MS-B respondió HTTP {(int)resp.StatusCode} al pedir el contacto.");
            var dto = await resp.Content.ReadFromJsonAsync<ContactoDto>(cancellationToken: ct);
            return dto is null ? (null, "Respuesta vacía de MS-B.") : (new ContactoEstudiante(dto.Nombre, dto.Email), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (null, "No se pudo consultar el correo del estudiante en MS-B.");
        }
    }

    private sealed record ContactoDto(int EstudianteId, string Nombre, string? Email);
}

/// <summary>
/// Envío por SMTP con MailKit. Si Smtp:Host está vacío falla con un mensaje claro en vez de simular un envío.
/// </summary>
public sealed class MailKitEnviadorCorreo(IOptions<SmtpOptions> options, ILogger<MailKitEnviadorCorreo> logger) : IEnviadorCorreo
{
    public async Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host))
            throw new CorreoNoDisponibleException("El envío de correo no está configurado (Smtp:Host). Revise SMTP_HOST en el archivo .env.");

        try
        {
            var m = new MimeKit.MimeMessage();
            m.From.Add(new MimeKit.MailboxAddress(o.FromName, o.From));
            m.To.Add(new MimeKit.MailboxAddress(mensaje.NombreDestino, mensaje.Para));
            m.Subject = mensaje.Asunto;
            m.Body = new MimeKit.TextPart("plain") { Text = mensaje.Cuerpo };

            using var cliente = new MailKit.Net.Smtp.SmtpClient();
            await cliente.ConnectAsync(o.Host, o.Port,
                o.UseStartTls ? MailKit.Security.SecureSocketOptions.StartTls : MailKit.Security.SecureSocketOptions.None, ct);
            if (!string.IsNullOrWhiteSpace(o.User))
                await cliente.AuthenticateAsync(o.User, o.Password ?? string.Empty, ct);
            await cliente.SendAsync(m, ct);
            await cliente.DisconnectAsync(true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo enviar el correo.");
            throw new CorreoNoDisponibleException("No se pudo entregar el correo: " + ex.Message, ex);
        }
    }
}

/// <summary>Redacta el correo con el contenido APROBADO por el docente. No incluye datos de otros estudiantes.</summary>
public static class CorreoRecomendacion
{
    public static MensajeCorreo Componer(string nombreEstudiante, string email, string materia, string docenteEmail, ContenidoRecomendacionDto c)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hola {nombreEstudiante},");
        sb.AppendLine();
        sb.AppendLine($"Tu docente de «{materia}» revisó tu avance y te comparte esta orientación:");
        Seccion(sb, "Aspectos a reforzar", c.Falencias);
        Seccion(sb, "Actividades sugeridas", c.ActividadesRefuerzo);
        Seccion(sb, "Contenidos a repasar", c.ContenidosARepasar);
        if (!string.IsNullOrWhiteSpace(c.Observacion)) { sb.AppendLine(); sb.AppendLine("Observación: " + c.Observacion); }
        sb.AppendLine();
        sb.AppendLine("Estas sugerencias son orientativas y fueron revisadas y enviadas manualmente por tu docente.");
        sb.AppendLine($"Si tienes dudas, responde directamente a tu docente ({docenteEmail}).");
        return new MensajeCorreo(email, nombreEstudiante, $"Orientación académica · {materia}", sb.ToString());
    }

    private static void Seccion(StringBuilder sb, string titulo, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine(titulo + ":");
        foreach (var i in items) sb.AppendLine("  • " + i);
    }
}

/// <summary>
/// HU-20 (opcional). Solo se envía una recomendación APROBADA y solo por acción manual del docente
/// (este servicio únicamente se invoca desde el endpoint POST «enviar»; no hay tareas automáticas).
/// </summary>
public class EnvioRecomendacionService(
    AnalyticsDbContext db, RecomendacionService recomendaciones, IContactoEstudiante contactos, IEnviadorCorreo correo, IAuditClient audit)
{
    private static readonly Regex FormatoCorreo = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public async Task<Resultado<EnvioRecomendacionDto>> EnviarAsync(int materiaId, int estudianteId, string? email, CancellationToken ct = default)
    {
        var acceso = await recomendaciones.AutorizarAsync(materiaId, email, ct);
        if (!acceso.EsOk) return Resultado<EnvioRecomendacionDto>.Falla(acceso.Codigo, acceso.Mensaje!);

        var r = await db.Recomendaciones.FirstOrDefaultAsync(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId, ct);
        if (r is null) return Resultado<EnvioRecomendacionDto>.Falla(CodigoResultado.NoEncontrado, "Este estudiante todavía no tiene una recomendación.");
        if (r.Estado != EstadosRecomendacion.Aprobada)
            return Resultado<EnvioRecomendacionDto>.Falla(CodigoResultado.Conflicto, "La recomendación no está aprobada: apruébala antes de enviarla.");

        var (contacto, error) = await contactos.ObtenerAsync(materiaId, estudianteId, ct);
        if (contacto is null) return Resultado<EnvioRecomendacionDto>.Falla(CodigoResultado.NoEncontrado, error ?? "No se encontró al estudiante.");
        if (string.IsNullOrWhiteSpace(contacto.Email) || !FormatoCorreo.IsMatch(contacto.Email.Trim()))
            return Resultado<EnvioRecomendacionDto>.Falla(CodigoResultado.Invalido, "El estudiante no tiene un correo válido registrado en Moodle.");

        var mensaje = CorreoRecomendacion.Componer(contacto.Nombre, contacto.Email.Trim(), acceso.Valor!.Nombre, email!.Trim().ToLowerInvariant(),
            RecomendacionService.LeerContenido(r));

        var mascara = AuditSanitizer.EnmascararCorreo(contacto.Email);
        try
        {
            await correo.EnviarAsync(mensaje, ct);
        }
        catch (CorreoNoDisponibleException ex)
        {
            await audit.RegistrarAsync("RECOMENDACION_ENVIO_FALLIDO", "MS-C", $"Materia {materiaId} · estudiante {estudianteId} · {ex.Message}", ct);
            throw;
        }

        r.EnviadaUtc = DateTime.UtcNow;
        r.EnviadaA = mascara;
        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("RECOMENDACION_ENVIADA", "MS-C", $"Materia {materiaId} · estudiante {estudianteId} · a {mascara}", ct);
        return Resultado<EnvioRecomendacionDto>.Ok(new EnvioRecomendacionDto(r.Id, mascara, r.EnviadaUtc.Value));
    }
}
