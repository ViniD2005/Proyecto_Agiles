using System.Text.Json;
using EduMonitor.Analytics.Api.Services;
using EduMonitor.Common.Messaging;
using Microsoft.Extensions.Options;

namespace EduMonitor.Analytics.Api.Messaging;

/// <summary>
/// T10.3 — Consume CalificacionesSincronizadas (recalcula indicadores y alertas)
/// y MapeoActividadActualizado (actualiza el desempeño por tema y contenido).
/// </summary>
public class AnalyticsEventConsumer(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<AnalyticsEventConsumer> logger)
    : RabbitMqConsumerBase(options, logger, RabbitMqTopology.QueueAnalitica)
{
    protected override async Task HandleAsync(string routingKey, byte[] body, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var ingestor = scope.ServiceProvider.GetRequiredService<SnapshotIngestor>();

        switch (routingKey)
        {
            case EventNames.CalificacionesSincronizadas:
                var calificaciones = JsonSerializer.Deserialize<CalificacionesSincronizadas>(body, RabbitMqJson.Options)
                    ?? throw new InvalidOperationException("Evento de calificaciones vacío.");
                await ingestor.IngerirCalificacionesAsync(calificaciones, ct);
                break;

            case EventNames.MapeoActividadActualizado:
                var mapeo = JsonSerializer.Deserialize<MapeoActividadActualizado>(body, RabbitMqJson.Options)
                    ?? throw new InvalidOperationException("Evento de mapeo vacío.");
                await ingestor.IngerirMapeoAsync(mapeo, ct);
                break;

            default:
                logger.LogWarning("Evento desconocido {Evento}: se ignora", routingKey);
                break;
        }
    }
}
