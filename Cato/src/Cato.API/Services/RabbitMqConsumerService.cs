using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Cato.API.Services;

/// <summary>
/// BackgroundService that listens to the RabbitMQ ingestion queue and dispatches
/// messages to the appropriate ingestion handler based on the "source" field.
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
        // Retry connection until RabbitMQ is ready
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

        // Declare exchange and queue
        await channel.ExchangeDeclareAsync(
            exchange: _settings.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: _settings.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Bind queue to all ingestion routing keys
        await channel.QueueBindAsync(
            queue: _settings.QueueName,
            exchange: _settings.ExchangeName,
            routingKey: "ingestion.#",
            cancellationToken: stoppingToken);

        // Process one message at a time
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = ea.Body.ToArray();
            var messageJson = Encoding.UTF8.GetString(body);

            _logger.LogInformation("Received message on {RoutingKey}: {Message}",
                ea.RoutingKey, messageJson);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IngestionDispatcher>();
                await dispatcher.DispatchAsync(messageJson, stoppingToken);

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                _logger.LogInformation("Message processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message: {Message}", messageJson);
                // Reject and requeue=false (send to dead-letter or discard)
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            queue: _settings.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Listening on queue '{Queue}' for ingestion messages...", _settings.QueueName);

        // Keep alive until cancellation
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
}
