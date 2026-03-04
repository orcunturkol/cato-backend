namespace Cato.Infrastructure.Messaging;

/// <summary>
/// Abstracts message routing so RabbitMqConsumerService (Infrastructure) does not
/// depend directly on application-layer types.
/// </summary>
public interface IIngestionDispatcher
{
    Task DispatchAsync(string messageJson, CancellationToken ct);
}
