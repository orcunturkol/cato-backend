using Cato.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Cato.API.Services.Alerts;

public sealed record QueueStats(string Queue, uint Messages, uint Consumers);

public interface IQueueStatsReader
{
    /// <summary>The DLQ and the consumed queues. Throws when the broker cannot be read.</summary>
    Task<IReadOnlyList<QueueStats>> ReadAsync(CancellationToken ct);
}

/// <summary>
/// Reads message and consumer counts with a passive queue declare: plain AMQP, so it works
/// on Amazon MQ too (no management API there). Its own connection, opened on first use.
/// </summary>
public sealed class RabbitQueueStatsReader : IQueueStatsReader, IAsyncDisposable
{
    private readonly RabbitMqSettings _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public RabbitQueueStatsReader(IOptions<RabbitMqSettings> settings) => _settings = settings.Value;

    public IReadOnlyList<string> Queues =>
    [
        _settings.DeadLetterQueue, _settings.QueueName, _settings.QueueNameV2, _settings.GameAnalyzedQueue,
    ];

    public async Task<IReadOnlyList<QueueStats>> ReadAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is not { IsOpen: true })
            {
                if (_connection is not null)
                    await _connection.DisposeAsync();
                var factory = RabbitMqConsumerService.CreateConnectionFactory(_settings);
                factory.ClientProvidedName = $"{_settings.ClientProvidedName}-alerts";
                _connection = await factory.CreateConnectionAsync(ct);
            }

            var result = new List<QueueStats>();
            foreach (var queue in Queues)
            {
                // A passive declare of a missing queue closes the channel, so one channel each.
                await using var channel = await _connection.CreateChannelAsync(cancellationToken: ct);
                var ok = await channel.QueueDeclarePassiveAsync(queue, ct);
                result.Add(new QueueStats(queue, ok.MessageCount, ok.ConsumerCount));
            }
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
