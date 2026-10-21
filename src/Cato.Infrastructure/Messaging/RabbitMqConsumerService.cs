using System.Net.Security;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Cato.Infrastructure.Messaging;

/// <summary>
/// BackgroundService that listens to three queues:
/// <see cref="RabbitMqSettings.QueueName"/> (legacy, routing key
/// <c>ingestion.{source}</c>) goes to <see cref="IIngestionDispatcher"/>;
/// <see cref="RabbitMqSettings.QueueNameV2"/> (routing key
/// <c>ingestion.batch.{source}</c>) goes to <see cref="IBatchIngestionDispatcher"/>;
/// <see cref="RabbitMqSettings.GameAnalyzedQueue"/> (routing key
/// <c>game.analyzed.{source}</c>) goes to <see cref="IGameAnalyzedDispatcher"/>.
/// Each queue has its own channel (dispatch concurrency 1, prefetch 1) on one shared
/// connection, so a slow handler holds up only its own queue. The connection recovers
/// its topology and consumers after a broker restart.
/// Nacks from any path are dead-lettered to the DLX; a message cut short by shutdown
/// is left unacked, and the broker redelivers it.
/// </summary>
public class RabbitMqConsumerService : BackgroundService
{
    /// <summary>Which dispatcher a delivery belongs to.</summary>
    internal enum DispatchTarget
    {
        Legacy,
        Batch,
        GameAnalyzed
    }

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

    /// <summary>
    /// Maps the settings to a connection factory. A non-empty <see cref="RabbitMqSettings.Uri"/>
    /// overrides host, port, user and password; <c>amqps</c> turns TLS on.
    /// </summary>
    public static ConnectionFactory CreateConnectionFactory(RabbitMqSettings settings)
    {
        var factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            UserName = settings.UserName,
            Password = settings.Password,
            ClientProvidedName = settings.ClientProvidedName,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        if (!string.IsNullOrWhiteSpace(settings.Uri))
        {
            factory.Uri = new Uri(settings.Uri);
            // The client's amqps parsing accepts a certificate for another host name; verify it.
            factory.Ssl.AcceptablePolicyErrors = SslPolicyErrors.None;
        }

        return factory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Outside the retry loop: a malformed Uri fails the start instead of retrying forever.
        var factory = CreateConnectionFactory(_settings);

        IConnection? connection = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                connection = await factory.CreateConnectionAsync(stoppingToken);
                _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port} as {ClientName}",
                    factory.HostName, factory.Port, factory.ClientProvidedName);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to RabbitMQ. Retrying in 5s...");
                await Task.Delay(5000, stoppingToken);
            }
        }

        if (connection is null) return;

        var channels = new List<IChannel>();
        try
        {
            await using (var setup = await connection.CreateChannelAsync(cancellationToken: stoppingToken))
            {
                await DeclareTopologyAsync(setup, stoppingToken);
                await setup.CloseAsync(stoppingToken);
            }

            channels.Add(await ConsumeAsync(connection, _settings.QueueName, DispatchTarget.Legacy, stoppingToken));
            foreach (var route in BatchQueues.For(_settings))
                channels.Add(await ConsumeAsync(connection, route.Queue, DispatchTarget.Batch, stoppingToken));
            channels.Add(await ConsumeAsync(connection, _settings.GameAnalyzedQueue, DispatchTarget.GameAnalyzed, stoppingToken));

            _logger.LogInformation(
                "Listening on queues '{Legacy}' (single), {Batch} (batch) and '{Analyzed}' (game.analyzed), one channel each; DLX='{DLX}'",
                _settings.QueueName, string.Join(", ", BatchQueues.For(_settings).Select(r => r.Queue)),
                _settings.GameAnalyzedQueue, _settings.DeadLetterExchange);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("RabbitMQ consumer stopping...");
        }
        finally
        {
            // Not the stopping token: it is already cancelled, and the close must still run.
            try
            {
                await connection.CloseAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMQ connection did not close cleanly");
            }

            foreach (var channel in channels)
                await channel.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    internal async Task DeclareTopologyAsync(IChannel channel, CancellationToken stoppingToken)
    {
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

        // ── Batch queues, one per source group, with DLX args ─────────────
        // Every explicit binding exists before the old wildcard goes: the collector publishes
        // a batch with no route nowhere (or, with mandatory=True, as a failure).
        foreach (var route in BatchQueues.For(_settings))
        {
            await channel.QueueDeclareAsync(
                queue: route.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"]    = _settings.DeadLetterExchange,
                    ["x-dead-letter-routing-key"] = "ingestion.batch.dead"
                },
                cancellationToken: stoppingToken);

            foreach (var source in route.Sources)
                await channel.QueueBindAsync(
                    queue: route.Queue,
                    exchange: _settings.ExchangeName,
                    routingKey: BatchQueues.RoutingKey(source),
                    cancellationToken: stoppingToken);
        }

        // Until 2026-10 every source went to v2 through this wildcard; with it the per-source
        // queues would get each batch twice. Unbinding a missing binding is a no-op.
        await channel.QueueUnbindAsync(
            queue: _settings.QueueNameV2,
            exchange: _settings.ExchangeName,
            routingKey: BatchQueues.LegacyWildcard,
            cancellationToken: stoppingToken);

        // ── game.analyzed queue (reddit_metrics events) with DLX args ──────
        await channel.QueueDeclareAsync(
            queue: _settings.GameAnalyzedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"]    = _settings.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = "game.analyzed.dead"
            },
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(
            queue: _settings.GameAnalyzedQueue,
            exchange: _settings.ExchangeName,
            routingKey: "game.analyzed.#",
            cancellationToken: stoppingToken);
    }

    /// <summary>
    /// Opens a channel for one queue and starts its consumer. The client runs a channel's
    /// handlers one at a time, so on one shared channel a slow handler stalled every queue.
    /// </summary>
    private async Task<IChannel> ConsumeAsync(
        IConnection connection, string queue, DispatchTarget target, CancellationToken stoppingToken)
    {
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: 1),
            stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, target, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: queue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        return channel;
    }

    internal async Task HandleAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        DispatchTarget target,
        CancellationToken stoppingToken)
    {
        var body = ea.Body.ToArray();
        var messageJson = Encoding.UTF8.GetString(body);

        _logger.LogInformation(
            "Received message on routingKey={RoutingKey} (target={Target}) size={Size}",
            ea.RoutingKey, target, body.Length);

        // Ack and nack take CancellationToken.None: the stopping token must not skip them.
        try
        {
            using var scope = _serviceProvider.CreateScope();
            switch (target)
            {
                case DispatchTarget.Batch:
                    await scope.ServiceProvider.GetRequiredService<IBatchIngestionDispatcher>()
                        .DispatchAsync(messageJson, stoppingToken);
                    break;
                case DispatchTarget.GameAnalyzed:
                    await scope.ServiceProvider.GetRequiredService<IGameAnalyzedDispatcher>()
                        .DispatchAsync(messageJson, stoppingToken);
                    break;
                default:
                    await scope.ServiceProvider.GetRequiredService<IIngestionDispatcher>()
                        .DispatchAsync(messageJson, stoppingToken);
                    break;
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a bad message: a nack would dead-letter it. Unacked, the broker
            // redelivers it when the connection closes.
            _logger.LogInformation(
                "Shutdown interrupted message on {RoutingKey}; leaving it for redelivery", ea.RoutingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message on {RoutingKey}; nacking to DLX", ea.RoutingKey);
            await channel.BasicNackAsync(
                ea.DeliveryTag, multiple: false, requeue: false,
                cancellationToken: CancellationToken.None);
        }
    }
}
