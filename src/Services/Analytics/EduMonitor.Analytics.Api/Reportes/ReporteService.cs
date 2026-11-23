using System.Text.Encodings.Web;
using System.Text.Json;
using EduMonitor.Analytics.Api.Agente;
using EduMonitor.Analytics.Api.Agente.Llm;
using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Domain;
using EduMonitor.Analytics.Api.Services;
using EduMonitor.Common.Auditoria;
using EduMonitor.Common.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduMonitor.Analytics.Api.Reportes;

public sealed class ReporteOptions
{
    public const string Section = "Reportes";
    /// <summary>Texto del período que aparece en el reporte (ej. «Primer parcial»).</summary>
    public string Periodo { get; set; } = "Primer parcial";
}

public interface IGeneradorPdfReporte
{
    byte[] Generar(ReporteDto reporte);
}

/// <summary>
/// HU-16: reporte inteligente del curso. Las cifras salen de AnalisisCursoService (las mismas del dashboard, criterio 4);
/// los hallazgos y recomendaciones grupales son reglas del sistema; el análisis del modelo es opcional y va rotulado.
/// El contenido se guarda al generarlo para que la vista previa y el PDF muestren exactamente lo mismo.
/// </summary>
public class ReporteService(
    AnalyticsDbContext db, AnalisisCursoService analisis, ILlmProvider llm, IGeneradorPdfReporte pdf,
    IOptions<ReporteOptions> opciones, IAuditClient audit, ILogger<ReporteService> logger)
{
    public const string Aviso = "Las recomendaciones son orientativas y deben ser revisadas por el docente.";
    public const string Fuente = "Moodle";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<Resultado<ReporteDto>> GenerarAsync(int materiaId, string? email, CancellationToken ct = default)
    {
        var r = await analisis.ObtenerAsync(materiaId, email, ct);   // valida que la materia sea del docente (RN-07)
        if (!r.EsOk) return Resultado<ReporteDto>.Falla(r.Codigo, r.Mensaje ?? "No se pudo analizar la materia.");
        var a = r.Valor!;
        if (a.Curso.Estudiantes == 0)
            return Resultado<ReporteDto>.Falla(CodigoResultado.Invalido, "La materia no tiene estudiantes: no hay datos para el reporte.");

        var analisisIa = await RedactarAnalisisAsync(a, ct);

        var temas = a.Temas.Where(t => t.Promedio.HasValue).Take(6)
            .Select(t => new TemaReporteDto(t.Tema, t.Unidad, t.Promedio, t.Nivel, t.EstudiantesCriticos)).ToList();
        var ahora = DateTime.UtcNow;
        var sinId = new ReporteDto(
            0, materiaId, a.Materia, opciones.Value.Periodo, Fuente, ahora, a.ActualizadoUtc, email!.Trim().ToLowerInvariant(),
            a.Curso, a.Umbrales, temas, ReglasCurso.Hallazgos(a), ReglasCurso.Recomendaciones(a), analisisIa, Aviso);

        var entidad = new ReporteCurso
        {
            MateriaId = materiaId, GeneradoUtc = ahora, GeneradoPor = sinId.GeneradoPor,
            Contenido = JsonSerializer.Serialize(sinId, Json)
        };
        db.Reportes.Add(entidad);
        await db.SaveChangesAsync(ct);

        await audit.RegistrarAsync("REPORTE_GENERADO", "MS-C",
            $"Materia {materiaId} · reporte {entidad.Id} · análisis IA {(analisisIa is null ? "no" : "sí")}", ct);
        return Resultado<ReporteDto>.Ok(sinId with { ReporteId = entidad.Id });
    }

    public async Task<Resultado<ReporteDto>> UltimoAsync(int materiaId, string? email, CancellationToken ct = default)
    {
        var acceso = await AutorizarMateriaAsync(materiaId, email, ct);
        if (acceso is not null) return Resultado<ReporteDto>.Falla(acceso.Value.Codigo, acceso.Value.Mensaje);

        var ultimo = (await db.Reportes.AsNoTracking().Where(x => x.MateriaId == materiaId).ToListAsync(ct))
            .OrderByDescending(x => x.GeneradoUtc).ThenByDescending(x => x.Id).FirstOrDefault();
        if (ultimo is null)
            return Resultado<ReporteDto>.Falla(CodigoResultado.NoEncontrado, "Todavía no hay un reporte generado para esta materia.");
        return Resultado<ReporteDto>.Ok(Leer(ultimo));
    }

    public async Task<Resultado<(byte[] Bytes, string Nombre)>> ObtenerPdfAsync(long reporteId, string? email, CancellationToken ct = default)
    {
        var entidad = await db.Reportes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == reporteId, ct);
        if (entidad is null)
            return Resultado<(byte[], string)>.Falla(CodigoResultado.NoEncontrado, "El reporte no existe.");
        var acceso = await AutorizarMateriaAsync(entidad.MateriaId, email, ct);
        if (acceso is not null) return Resultado<(byte[], string)>.Falla(acceso.Value.Codigo, acceso.Value.Mensaje);

        var dto = Leer(entidad);
        var bytes = pdf.Generar(dto);
        await audit.RegistrarAsync("REPORTE_DESCARGADO", "MS-C", $"Materia {entidad.MateriaId} · reporte {entidad.Id}", ct);
        return Resultado<(byte[], string)>.Ok((bytes, NombreArchivo(dto)));
    }

    public static string NombreArchivo(ReporteDto d)
    {
        var limpio = new string(d.Materia.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (limpio.Length == 0) limpio = "curso";
        return $"reporte_{limpio}_{d.GeneradoUtc:yyyyMMdd_HHmm}.pdf";
    }

    private static ReporteDto Leer(ReporteCurso e) =>
        JsonSerializer.Deserialize<ReporteDto>(e.Contenido, Json)! with { ReporteId = e.Id };

    private async Task<(CodigoResultado Codigo, string Mensaje)?> AutorizarMateriaAsync(int materiaId, string? email, CancellationToken ct)
    {
        var m = await db.Materias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == materiaId, ct);
        if (m is null) return (CodigoResultado.NoEncontrado, "La materia no existe.");
        if (string.IsNullOrWhiteSpace(email) || !string.Equals(m.DocenteEmail, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return (CodigoResultado.Prohibido, "No tienes permiso para este reporte.");
        return null;
    }

    /// <summary>Párrafo breve del modelo sobre el curso. Solo recibe cifras agregadas: ningún dato de estudiantes individuales.</summary>
    private async Task<string?> RedactarAnalisisAsync(AnalisisCurso a, CancellationToken ct)
    {
        try
        {
            var hechos = string.Join("\n", ReglasCurso.Hallazgos(a).Select(h => "- " + h));
            var sistema =
                "Eres un asistente pedagógico. Redacta en español un análisis breve (máximo 3 frases) del estado del curso usando SOLO los hechos entregados. " +
                "No inventes cifras, temas ni estudiantes. El análisis es orientativo. " +
                "Devuelve SOLO un objeto JSON: {\"analisis\":\"...\"}";
            var usuario = $"CURSO: {a.Materia}\nPROMEDIO: {CalculadoraIndicadores.Fmt(a.Curso.Promedio ?? 0)}\nHECHOS:\n{hechos}";
            var respuesta = await llm.CompletarAsync(new LlmSolicitud(new[]
            {
                new LlmMensaje(LlmMensaje.Sistema, sistema), new LlmMensaje(LlmMensaje.Usuario, usuario)
            }, SalidaJson: true), ct);

            var obj = SalidaModelo.ExtraerObjeto(respuesta.Texto);
            var texto = obj is null ? null : SalidaModelo.Lista(obj.Value, "analisis").FirstOrDefault();
            return string.IsNullOrWhiteSpace(texto) ? null : texto;
        }
        catch (LlmNoDisponibleException ex)
        {
            logger.LogInformation("Reporte sin análisis del modelo: {Mensaje}", ex.Message);
            return null;
        }
    }
}
