using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EduMonitor.Analytics.Api.Agente.Llm;

/// <summary>Manejo común de errores: cualquier fallo de red, tiempo o formato se traduce a LlmNoDisponibleException.</summary>
public abstract class LlmProviderBase(HttpClient http, IOptions<LlmOptions> options, ILogger logger) : ILlmProvider
{
    protected LlmOptions Opciones => options.Value;
    protected HttpClient Http => http;

    public abstract string Nombre { get; }
    public string Modelo => Opciones.Model;

    protected abstract HttpRequestMessage Construir(LlmSolicitud solicitud);
    protected abstract string? ExtraerTexto(JsonElement raiz);
    public abstract Task<EstadoLlm> ConsultarEstadoAsync(CancellationToken ct = default);

    public async Task<LlmRespuesta> CompletarAsync(LlmSolicitud solicitud, CancellationToken ct = default)
    {
        try
        {
            using var req = Construir(solicitud);
            using var resp = await http.SendAsync(req, ct);
            var cuerpo = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("{Proveedor} respondió HTTP {Codigo}: {Cuerpo}", Nombre, (int)resp.StatusCode,
                    cuerpo.Length > 300 ? cuerpo[..300] : cuerpo);
                throw new LlmNoDisponibleException($"{Nombre} respondió con error HTTP {(int)resp.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(cuerpo);
            var texto = ExtraerTexto(doc.RootElement);
            if (string.IsNullOrWhiteSpace(texto))
                throw new LlmNoDisponibleException($"{Nombre} devolvió una respuesta vacía.");
            return new LlmRespuesta(texto.Trim());
        }
        catch (LlmNoDisponibleException) { throw; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LlmNoDisponibleException($"{Nombre} no respondió dentro del tiempo límite ({Opciones.TimeoutSeconds} s).");
        }
        catch (HttpRequestException ex)
        {
            throw new LlmNoDisponibleException($"No se pudo conectar con {Nombre}: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new LlmNoDisponibleException($"{Nombre} devolvió un formato inesperado.", ex);
        }
    }

    protected static StringContent Json(object cuerpo) =>
        new(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");

    protected static object[] Mensajes(LlmSolicitud s) =>
        s.Mensajes.Select(m => (object)new { role = m.Rol, content = m.Contenido }).ToArray();
}

/// <summary>Ollama local (T13.1 / T13.2). POST /api/chat sin streaming; formato JSON opcional.</summary>
public sealed class OllamaProvider(HttpClient http, IOptions<LlmOptions> options, ILogger<OllamaProvider> logger)
    : LlmProviderBase(http, options, logger)
{
    public override string Nombre => "Ollama";

    protected override HttpRequestMessage Construir(LlmSolicitud s)
    {
        var cuerpo = new Dictionary<string, object?>
        {
            ["model"] = Opciones.Model,
            ["messages"] = Mensajes(s),
            ["stream"] = false,
            ["keep_alive"] = "10m",
            ["options"] = new { temperature = Opciones.Temperature, num_predict = Opciones.MaxTokens, num_ctx = Opciones.ContextTokens }
        };
        if (s.SalidaJson) cuerpo["format"] = "json";
        return new HttpRequestMessage(HttpMethod.Post, "api/chat") { Content = Json(cuerpo) };
    }

    protected override string? ExtraerTexto(JsonElement raiz) =>
        raiz.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c) ? c.GetString() : null;

    /// <summary>Disponible si Ollama responde y el modelo configurado ya fue descargado (ollama pull).</summary>
    public override async Task<EstadoLlm> ConsultarEstadoAsync(CancellationToken ct = default)
    {
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await Http.GetAsync("api/tags", limite.Token);
            if (!resp.IsSuccessStatusCode) return new EstadoLlm(false, $"Ollama respondió HTTP {(int)resp.StatusCode}.");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(limite.Token));
            var instalados = doc.RootElement.TryGetProperty("models", out var lista)
                ? lista.EnumerateArray().Select(x => x.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").ToList()
                : new List<string>();
            var buscado = Opciones.Model.Contains(':') ? Opciones.Model : Opciones.Model + ":latest";
            return instalados.Any(n => string.Equals(n, buscado, StringComparison.OrdinalIgnoreCase))
                ? new EstadoLlm(true, null)
                : new EstadoLlm(false, $"Ollama está activo pero el modelo «{Opciones.Model}» no está descargado (ollama pull {Opciones.Model}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return new EstadoLlm(false, "No se pudo conectar con Ollama. Verifique que esté en ejecución.");
        }
    }
}

/// <summary>Google Gemini (alternativa de T13.1). La clave viaja en el encabezado, nunca en la URL.</summary>
public sealed class GeminiProvider(HttpClient http, IOptions<LlmOptions> options, ILogger<GeminiProvider> logger)
    : LlmProviderBase(http, options, logger)
{
    public override string Nombre => "Gemini";

    protected override HttpRequestMessage Construir(LlmSolicitud s)
    {
        var sistema = string.Join("\n\n", s.Mensajes.Where(m => m.Rol == LlmMensaje.Sistema).Select(m => m.Contenido));
        var contenidos = s.Mensajes.Where(m => m.Rol != LlmMensaje.Sistema)
            .Select(m => new { role = m.Rol == LlmMensaje.Asistente ? "model" : "user", parts = new[] { new { text = m.Contenido } } })
            .ToArray();
        var generacion = new Dictionary<string, object?>
        {
            ["temperature"] = Opciones.Temperature,
            ["maxOutputTokens"] = Opciones.MaxTokens
        };
        if (s.SalidaJson) generacion["responseMimeType"] = "application/json";

        var cuerpo = new Dictionary<string, object?> { ["contents"] = contenidos, ["generationConfig"] = generacion };
        if (sistema.Length > 0) cuerpo["systemInstruction"] = new { parts = new[] { new { text = sistema } } };

        var req = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(Opciones.Model)}:generateContent") { Content = Json(cuerpo) };
        req.Headers.Add("x-goog-api-key", Opciones.ApiKey ?? string.Empty);
        return req;
    }

    protected override string? ExtraerTexto(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("candidates", out var c) || c.GetArrayLength() == 0) return null;
        var contenido = c[0].TryGetProperty("content", out var cont) ? cont : default;
        if (contenido.ValueKind != JsonValueKind.Object || !contenido.TryGetProperty("parts", out var partes) || partes.GetArrayLength() == 0) return null;
        return partes[0].TryGetProperty("text", out var t) ? t.GetString() : null;
    }

    public override Task<EstadoLlm> ConsultarEstadoAsync(CancellationToken ct = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(Opciones.ApiKey)
            ? new EstadoLlm(false, "Falta Llm:ApiKey (LLM_API_KEY) para Gemini.")
            : new EstadoLlm(true, null));
}

/// <summary>Groq (API compatible con OpenAI). También sirve para otros servicios con el mismo formato.</summary>
public sealed class GroqProvider(HttpClient http, IOptions<LlmOptions> options, ILogger<GroqProvider> logger)
    : LlmProviderBase(http, options, logger)
{
    public override string Nombre => "Groq";

    protected override HttpRequestMessage Construir(LlmSolicitud s)
    {
        var cuerpo = new Dictionary<string, object?>
        {
            ["model"] = Opciones.Model,
            ["messages"] = Mensajes(s),
            ["temperature"] = Opciones.Temperature,
            ["max_tokens"] = Opciones.MaxTokens
        };
        if (s.SalidaJson) cuerpo["response_format"] = new { type = "json_object" };

        var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions") { Content = Json(cuerpo) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Opciones.ApiKey ?? string.Empty);
        return req;
    }

    protected override string? ExtraerTexto(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("choices", out var c) || c.GetArrayLength() == 0) return null;
        return c[0].TryGetProperty("message", out var m) && m.TryGetProperty("content", out var t) ? t.GetString() : null;
    }

    public override Task<EstadoLlm> ConsultarEstadoAsync(CancellationToken ct = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(Opciones.ApiKey)
            ? new EstadoLlm(false, "Falta Llm:ApiKey (LLM_API_KEY) para Groq.")
            : new EstadoLlm(true, null));
}

public static class LlmServiceCollectionExtensions
{
    /// <summary>Registra el proveedor elegido en la configuración (Llm:Provider).</summary>
    public static IServiceCollection AddLlmProvider(this IServiceCollection services, IConfiguration config)
    {
        var opciones = config.GetSection(LlmOptions.Section).Get<LlmOptions>() ?? new LlmOptions();
        services.Configure<LlmOptions>(config.GetSection(LlmOptions.Section));

        var proveedor = (opciones.Provider ?? "Ollama").Trim().ToLowerInvariant();
        var baseUrl = string.IsNullOrWhiteSpace(opciones.BaseUrl) ? proveedor switch
        {
            "gemini" => "https://generativelanguage.googleapis.com/",
            "groq" => "https://api.groq.com/openai/",
            _ => "http://localhost:11434/"
        } : opciones.BaseUrl;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(opciones.TimeoutSeconds, 5, 600));

        void Configurar(HttpClient h)
        {
            h.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            h.Timeout = timeout;
        }

        switch (proveedor)
        {
            case "ollama": services.AddHttpClient<ILlmProvider, OllamaProvider>(Configurar); break;
            case "gemini": services.AddHttpClient<ILlmProvider, GeminiProvider>(Configurar); break;
            case "groq": services.AddHttpClient<ILlmProvider, GroqProvider>(Configurar); break;
            default:
                throw new InvalidOperationException($"Llm:Provider «{opciones.Provider}» no es válido. Use Ollama, Gemini o Groq.");
        }
        return services;
    }
}
