namespace EduMonitor.Analytics.Api.Agente.Llm;

public record LlmMensaje(string Rol, string Contenido)
{
    public const string Sistema = "system", Usuario = "user", Asistente = "assistant";
}

/// <param name="SalidaJson">Pide al proveedor que devuelva solo JSON (Ollama format, Gemini responseMimeType, Groq json_object).</param>
public record LlmSolicitud(IReadOnlyList<LlmMensaje> Mensajes, bool SalidaJson = true);

public record LlmRespuesta(string Texto);

public record EstadoLlm(bool Disponible, string? Detalle);

/// <summary>El proveedor no respondió, respondió con error o devolvió algo inutilizable. El agente reacciona sin inventar datos.</summary>
public class LlmNoDisponibleException(string mensaje, Exception? inner = null) : Exception(mensaje, inner);

/// <summary>
/// HU-13, criterio 5: el resto del sistema solo conoce esta interfaz. Cambiar de Ollama a Gemini o Groq
/// es cambiar la configuración (Llm:Provider), sin tocar el agente, las recomendaciones ni los reportes.
/// </summary>
public interface ILlmProvider
{
    string Nombre { get; }
    string Modelo { get; }
    Task<LlmRespuesta> CompletarAsync(LlmSolicitud solicitud, CancellationToken ct = default);
    Task<EstadoLlm> ConsultarEstadoAsync(CancellationToken ct = default);
}

public sealed class LlmOptions
{
    public const string Section = "Llm";
    /// <summary>Ollama (local, por defecto), Gemini o Groq.</summary>
    public string Provider { get; set; } = "Ollama";
    public string Model { get; set; } = "qwen2.5:3b";
    /// <summary>Vacío = valor por defecto del proveedor (Ollama http://localhost:11434).</summary>
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 700;
    /// <summary>Ventana de contexto que se pide a Ollama (num_ctx); por defecto 4096 para que el prompt no se trunque.</summary>
    public int ContextTokens { get; set; } = 4096;
}
