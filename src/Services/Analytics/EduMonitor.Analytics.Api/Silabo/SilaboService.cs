using EduMonitor.Analytics.Api.Agente.Llm;
using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Data;
using EduMonitor.Common.Auditoria;
using EduMonitor.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Analytics.Api.Silabo;

public interface IExtractorTextoPdf
{
    /// <summary>Texto de todas las páginas. Lanza excepción si el PDF está dañado o protegido.</summary>
    string Extraer(Stream pdf);
}

/// <summary>
/// HU-21 (opcional). Lee el sílabo en PDF y PROPONE una estructura (unidades → temas → contenidos mínimos).
/// No guarda nada: el docente corrige la propuesta en pantalla y solo al confirmar se importa a MS-B,
/// así que si sale sin confirmar no queda nada guardado (criterio 3).
/// </summary>
public class SilaboService(AnalyticsDbContext db, ILlmProvider llm, IExtractorTextoPdf extractor, IAuditClient audit, ILogger<SilaboService> logger)
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const string Aviso = "Propuesta generada automáticamente: revísala y corrígela. No se guarda nada hasta que la confirmes.";

    /// <summary>Verifica la firma «%PDF-» al inicio del contenido (síncrono: Span no puede usarse en métodos async).</summary>
    internal static bool EsPdf(MemoryStream memoria)
    {
        var cabecera = memoria.GetBuffer().AsSpan(0, (int)Math.Min(5, memoria.Length));
        return cabecera.SequenceEqual("%PDF-"u8);
    }

    public async Task<Resultado<PropuestaSilaboDto>> ProponerAsync(int materiaId, string? email, Stream pdf, long longitud, CancellationToken ct = default)
    {
        var materia = await db.Materias.AsNoTracking().FirstOrDefaultAsync(m => m.Id == materiaId, ct);
        if (materia is null)
            return Resultado<PropuestaSilaboDto>.Falla(CodigoResultado.NoEncontrado, "La materia no tiene datos de análisis todavía. Sincronice las calificaciones desde «Mis materias».");
        if (string.IsNullOrWhiteSpace(email) || !string.Equals(materia.DocenteEmail, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return Resultado<PropuestaSilaboDto>.Falla(CodigoResultado.Prohibido, "No tienes permiso para esta materia.");

        if (longitud <= 0) return Resultado<PropuestaSilaboDto>.Falla(CodigoResultado.Invalido, "El archivo está vacío.");
        if (longitud > MaxBytes) return Resultado<PropuestaSilaboDto>.Falla(CodigoResultado.Invalido, "El PDF supera el tamaño máximo de 5 MB.");

        // Se copia a memoria (máx. 5 MB) para verificar la firma «%PDF-» antes de pasarlo al lector
        using var memoria = new MemoryStream();
        await pdf.CopyToAsync(memoria, ct);
        if (memoria.Length > MaxBytes) return Resultado<PropuestaSilaboDto>.Falla(CodigoResultado.Invalido, "El PDF supera el tamaño máximo de 5 MB.");
        if (!EsPdf(memoria))
            return Resultado<PropuestaSilaboDto>.Falla(CodigoResultado.Invalido, "El archivo no es un PDF válido.");
        memoria.Position = 0;

        string texto;
        try { texto = TextoSilabo.Normalizar(extractor.Extraer(memoria)); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo leer el PDF del sílabo.");
            return Resultado<PropuestaSilaboDto>.Falla(CodigoResultado.Invalido, "No se pudo leer el PDF. Verifica que no esté protegido ni dañado.");
        }
        if (texto.Length < 80)
            return Resultado<PropuestaSilaboDto>.Falla(CodigoResultado.Invalido, "El PDF no contiene texto legible (¿es una imagen escaneada?).");

        var advertencias = new List<string>();
        IReadOnlyList<SilaboUnidadDto>? propuesta = null;
        var fuente = "Modelo";
        try
        {
            var respuesta = await llm.CompletarAsync(new LlmSolicitud(PropuestaSilaboParser.Mensajes(texto), SalidaJson: true), ct);
            propuesta = PropuestaSilaboParser.ParsearModelo(respuesta.Texto);
            if (propuesta is null) advertencias.Add("El modelo no devolvió una estructura utilizable; se aplicó la lectura por patrones.");
        }
        catch (LlmNoDisponibleException ex)
        {
            logger.LogInformation("Sílabo sin modelo: {Mensaje}", ex.Message);
            advertencias.Add("El modelo de lenguaje no está disponible; se aplicó la lectura por patrones.");
        }

        if (propuesta is null)
        {
            fuente = "Patrones";
            var porPatrones = ExtractorSilaboPorPatrones.Extraer(texto);
            if (porPatrones.Count == 0)
                return Resultado<PropuestaSilaboDto>.Falla(CodigoResultado.Invalido,
                    "No se pudieron identificar unidades y temas en el sílabo. Cárgalos manualmente en la estructura temática.");
            propuesta = porPatrones;
        }

        if (propuesta.Any(u => u.Temas.Count == 0))
            advertencias.Add("Hay unidades sin temas: agrégalos antes de confirmar.");
        if (propuesta.Any(u => u.Temas.Any(t => t.Contenidos.Count == 0)))
            advertencias.Add("Hay temas sin contenidos mínimos: complétalos antes de confirmar (cada tema necesita al menos uno).");

        await audit.RegistrarAsync("SILABO_PROPUESTA", "MS-C",
            $"Materia {materiaId} · fuente {fuente} · {propuesta.Count} unidad(es); nada guardado hasta confirmar", ct);
        return Resultado<PropuestaSilaboDto>.Ok(new PropuestaSilaboDto(fuente, propuesta, advertencias, Aviso));
    }
}

/// <summary>
/// Lector de texto con PdfPig (UglyToad.PdfPig). Reconstruye las líneas agrupando las palabras por su posición vertical,
/// porque el sílabo se interpreta línea por línea (encabezados de unidad, temas numerados, viñetas).
/// </summary>
public sealed class PdfPigExtractor : IExtractorTextoPdf
{
    private const double ToleranciaLinea = 3.0;

    public string Extraer(Stream pdf)
    {
        using var documento = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var sb = new System.Text.StringBuilder();
        foreach (var pagina in documento.GetPages())
        {
            var palabras = pagina.GetWords().OrderByDescending(w => w.BoundingBox.Bottom).ToList();
            var linea = new List<UglyToad.PdfPig.Content.Word>();
            double? yLinea = null;

            void Cerrar()
            {
                if (linea.Count == 0) return;
                sb.AppendLine(string.Join(" ", linea.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
                linea.Clear();
            }

            foreach (var w in palabras)
            {
                if (yLinea is null || Math.Abs(w.BoundingBox.Bottom - yLinea.Value) > ToleranciaLinea)
                {
                    Cerrar();
                    yLinea = w.BoundingBox.Bottom;
                }
                linea.Add(w);
            }
            Cerrar();
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
