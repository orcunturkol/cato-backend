namespace Cato.Infrastructure.Messaging;

public interface IBatchIngestionDispatcher
{
    Task DispatchAsync(string messageJson, CancellationToken ct);
}
