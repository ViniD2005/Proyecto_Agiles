using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EduMonitor.Common.Messaging;

public class RabbitMqOptions
{
    public const string Section = "RabbitMq";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    /// <summary>Se configura por variable de entorno (RabbitMq__User / RabbitMq__Password).</summary>
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Exchange { get; set; } = "edumonitor.events";
}

public static class RabbitMqJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// Topología única del bus. Publicador y consumidor la declaran de forma idempotente,
/// así ningún mensaje se pierde sin importar qué servicio arranque primero.
/// </summary>
public static class RabbitMqTopology
{
    public const string QueueAnalitica = "analitica.eventos";
    public const string QueueAnaliticaDlq = "analitica.eventos.dlq";

    public static void Declare(IModel channel, string exchange)
    {
        channel.ExchangeDeclare(exchange, ExchangeType.Direct, durable: true);
        // Cola de mensajes fallidos (dead letter) para no perder eventos que no se pudieron procesar
        channel.QueueDeclare(QueueAnaliticaDlq, durable: true, exclusive: false, autoDelete: false, arguments: null);
        var args = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = "",
            ["x-dead-letter-routing-key"] = QueueAnaliticaDlq
        };
        channel.QueueDeclare(QueueAnalitica, durable: true, exclusive: false, autoDelete: false, arguments: args);
        channel.QueueBind(QueueAnalitica, exchange, EventNames.CalificacionesSincronizadas);
        channel.QueueBind(QueueAnalitica, exchange, EventNames.MapeoActividadActualizado);
    }

    public static ConnectionFactory CreateFactory(RabbitMqOptions o, string clientName, bool asyncConsumers = false) => new()
    {
        HostName = o.Host,
        Port = o.Port,
        UserName = o.User,
        Password = o.Password,
        ClientProvidedName = clientName,
        DispatchConsumersAsync = asyncConsumers,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(3),
        AutomaticRecoveryEnabled = true
    };
}

public interface IEventPublisher
{
    /// <summary>Publica un evento. Devuelve false (sin lanzar excepción) si el bus no está disponible.</summary>
    Task<bool> PublishAsync<T>(string routingKey, T message, CancellationToken ct = default);
}

public sealed class RabbitMqEventPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqEventPublisher> logger)
    : IEventPublisher, IDisposable
{
    private readonly RabbitMqOptions _opt = options.Value;
    private readonly object _lock = new();
    private IConnection? _connection;
    private IModel? _channel;

    public Task<bool> PublishAsync<T>(string routingKey, T message, CancellationToken ct = default)
    {
        try
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(message, RabbitMqJson.Options);
            lock (_lock)
            {
                var channel = EnsureChannel();
                var props = channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";
                props.MessageId = Guid.NewGuid().ToString();
                channel.BasicPublish(_opt.Exchange, routingKey, props, body);
            }
            logger.LogInformation("Evento {Evento} publicado ({Bytes} bytes)", routingKey, body.Length);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            // El fallo del bus no debe tumbar la operación del docente: se registra y se informa.
            logger.LogWarning(ex, "No se pudo publicar el evento {Evento}", routingKey);
            lock (_lock) ResetConnection();
            return Task.FromResult(false);
        }
    }

    private IModel EnsureChannel()
    {
        if (_channel is { IsOpen: true }) return _channel;
        ResetConnection();
        var factory = RabbitMqTopology.CreateFactory(_opt, "edumonitor-publisher");
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        RabbitMqTopology.Declare(_channel, _opt.Exchange);
        return _channel;
    }

    private void ResetConnection()
    {
        try { _channel?.Dispose(); } catch { /* ya cerrado */ }
        try { _connection?.Dispose(); } catch { /* ya cerrado */ }
        _channel = null;
        _connection = null;
    }

    public void Dispose()
    {
        lock (_lock) ResetConnection();
    }
}

/// <summary>Base de los consumidores: conecta, reintenta si el bus cae y confirma (ack) cada mensaje procesado.</summary>
public abstract class RabbitMqConsumerBase(IOptions<RabbitMqOptions> options, ILogger logger, string queueName) : BackgroundService
{
    private readonly RabbitMqOptions _opt = options.Value;

    protected abstract Task HandleAsync(string routingKey, byte[] body, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = RabbitMqTopology.CreateFactory(_opt, "edumonitor-consumer", asyncConsumers: true);
                using var connection = factory.CreateConnection();
                using var channel = connection.CreateModel();
                RabbitMqTopology.Declare(channel, _opt.Exchange);
                channel.BasicQos(0, 1, false);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += async (_, ea) =>
                {
                    try
                    {
                        await HandleAsync(ea.RoutingKey, ea.Body.ToArray(), stoppingToken);
                        channel.BasicAck(ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error procesando {Evento}; el mensaje pasa a la cola de errores", ea.RoutingKey);
                        channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                };
                channel.BasicConsume(queueName, autoAck: false, consumer);
                logger.LogInformation("Consumidor conectado a la cola {Cola}", queueName);

                var cerrada = new TaskCompletionSource();
                connection.ConnectionShutdown += (_, _) => cerrada.TrySetResult();
                using var registro = stoppingToken.Register(() => cerrada.TrySetResult());
                await cerrada.Task;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("RabbitMQ no disponible ({Mensaje}); reintento en 5 s", ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { /* apagado del servicio */ }
            }
        }
    }
}

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<RabbitMqOptions>(config.GetSection(RabbitMqOptions.Section));
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        return services;
    }
}
