using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Cato.Infrastructure.Messaging;

/// <summary>
/// BackgroundService that listens to both the legacy single-item queue and the
/// v2 batch queue. Messages on <see cref="RabbitMqSettings.QueueName"/> (routing
/// key <c>ingestion.{source}</c>) go to <see cref="IIngestionDispatcher"/>.
/// Messages on <see cref="RabbitMqSettings.QueueNameV2"/> (routing key
/// <c>ingestion.batch.{source}</c>) go to <see cref="IBatchIngestionDispatcher"/>.
/// Nacks from either path are dead-lettered to the DLX.
/// </summary>
public class RabbitMqConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMqSettings _settings;
    private readonly ILogger<RabbitMqConsumerService> _logger;

    public RabbitMqConsumerService(
        IServiceProvider serviceProvider,
        IOptions<RabbitMqSettings> settings,
        ILogger<RabbitMqConsumerService> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IConnection? connection = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = _settings.HostName,
                    Port = _settings.Port,
                    UserName = _settings.UserName,
                    Password = _settings.Password
                };

                connection = await factory.CreateConnectionAsync(stoppingToken);
                _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", _settings.HostName, _settings.Port);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to RabbitMQ. Retrying in 5s...");
                await Task.Delay(5000, stoppingToken);
            }
        }

        if (connection is null) return;

        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // ── Primary exchange ───────────────────────────────────────────────
        await channel.ExchangeDeclareAsync(
            exchange: _settings.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: stoppingToken);

        // ── Dead-letter exchange + queue ───────────────────────────────────
        await channel.ExchangeDeclareAsync(
            exchange: _settings.DeadLetterExchange,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: _settings.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(
            queue: _settings.DeadLetterQueue,
            exchange: _settings.DeadLetterExchange,
            routingKey: "#",
            cancellationToken: stoppingToken);

        // ── Legacy queue (single-item path, no DLX args — declared originally without) ──
        await channel.QueueDeclareAsync(
            queue: _settings.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(
            queue: _settings.QueueName,
            exchange: _settings.ExchangeName,
            routingKey: "ingestion.*",
            cancellationToken: stoppingToken);

        // ── V2 queue (batch path) with DLX args ────────────────────────────
        await channel.QueueDeclareAsync(
            queue: _settings.QueueNameV2,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"]    = _settings.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = "ingestion.batch.dead"
            },
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(
            queue: _settings.QueueNameV2,
            exchange: _settings.ExchangeName,
            routingKey: "ingestion.batch.#",
            cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        // ── Legacy consumer ────────────────────────────────────────────────
        var legacyConsumer = new AsyncEventingBasicConsumer(channel);
        legacyConsumer.ReceivedAsync += (_, ea) =>
            HandleAsync(channel, ea, dispatchBatch: false, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: _settings.QueueName,
            autoAck: false,
            consumer: legacyConsumer,
            cancellationToken: stoppingToken);

        // ── V2 (batch) consumer ────────────────────────────────────────────
        var batchConsumer = new AsyncEventingBasicConsumer(channel);
        batchConsumer.ReceivedAsync += (_, ea) =>
            HandleAsync(channel, ea, dispatchBatch: true, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: _settings.QueueNameV2,
            autoAck: false,
            consumer: batchConsumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Listening on queues '{Legacy}' (single) and '{V2}' (batch); DLX='{DLX}'",
            _settings.QueueName, _settings.QueueNameV2, _settings.DeadLetterExchange);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RabbitMQ consumer stopping...");
        }
        finally
        {
            await channel.CloseAsync(stoppingToken);
            await connection.CloseAsync(stoppingToken);
        }
    }

    private async Task HandleAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        bool dispatchBatch,
        CancellationToken stoppingToken)
    {
        var body = ea.Body.ToArray();
        var messageJson = Encoding.UTF8.GetString(body);

        _logger.LogInformation(
            "Received message on routingKey={RoutingKey} (batch={Batch}) size={Size}",
            ea.RoutingKey, dispatchBatch, body.Length);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            if (dispatchBatch)
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<IBatchIngestionDispatcher>();
                await dispatcher.DispatchAsync(messageJson, stoppingToken);
            }
            else
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<IIngestionDispatcher>();
                await dispatcher.DispatchAsync(messageJson, stoppingToken);
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message on {RoutingKey}; nacking to DLX", ea.RoutingKey);
            await channel.BasicNackAsync(
                ea.DeliveryTag, multiple: false, requeue: false,
                cancellationToken: stoppingToken);
        }
    }
}
