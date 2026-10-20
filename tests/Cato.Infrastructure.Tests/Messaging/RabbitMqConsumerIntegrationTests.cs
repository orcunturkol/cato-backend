using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Cato.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Xunit.Abstractions;

namespace Cato.Infrastructure.Tests.Messaging;

/// <summary>
/// Runs only against a disposable broker named by <c>CATO_RABBITMQ_TEST_URI</c>:
/// <code>
/// docker run -d --rm --name cato-rmq-test -p 127.0.0.1:56720:5672 rabbitmq:3-management
/// CATO_RABBITMQ_TEST_URI=amqp://guest:guest@127.0.0.1:56720/ \
/// CATO_RABBITMQ_TEST_CONTAINER=cato-rmq-test dotnet test
/// </code>
/// The restart test also needs <c>CATO_RABBITMQ_TEST_CONTAINER</c>: it runs <c>docker restart</c>.
/// </summary>
public sealed class RabbitMqFactAttribute : FactAttribute
{
    public const string UriVariable = "CATO_RABBITMQ_TEST_URI";
    public const string ContainerVariable = "CATO_RABBITMQ_TEST_CONTAINER";

    public RabbitMqFactAttribute(bool restartsBroker = false)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UriVariable)))
            Skip = $"Set {UriVariable} to a disposable RabbitMQ to run this test.";
        else if (restartsBroker && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ContainerVariable)))
            Skip = $"Set {ContainerVariable} to the broker's docker container to run this test.";
    }
}

/// <summary>
/// <see cref="RabbitMqConsumerService"/> against a real broker with fake dispatchers. Each test
/// uses its own exchanges and queues and deletes them afterwards.
/// </summary>
public sealed class RabbitMqConsumerIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RecoveryWait = TimeSpan.FromSeconds(90);

    private static string BrokerUri => Environment.GetEnvironmentVariable(RabbitMqFactAttribute.UriVariable)!;

    private readonly RabbitMqSettings _settings = NewSettings(Guid.NewGuid().ToString("N")[..8]);
    private readonly List<RabbitMqConsumerService> _services = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private static RabbitMqSettings NewSettings(string id) => new()
    {
        Uri = BrokerUri,
        ClientProvidedName = "cato-backend-test",
        QueueName = $"test-{id}-ingestion",
        QueueNameV2 = $"test-{id}-ingestion-v2",
        GameAnalyzedQueue = $"test-{id}-game-analyzed",
        ExchangeName = $"test-{id}-data",
        DeadLetterExchange = $"test-{id}-dlx",
        DeadLetterQueue = $"test-{id}-dlq"
    };

    private static Task Ok(string _, CancellationToken __) => Task.CompletedTask;

    [RabbitMqFact]
    public async Task A_blocked_batch_handler_does_not_stop_the_other_queues()
    {
        var release = NewSignal();
        var batchStarted = NewSignal();
        var legacyDone = NewSignal();
        var analyzedDone = NewSignal();
        int batches = 0, analyzed = 0;

        await StartServiceAsync(
            legacy: (_, _) => { legacyDone.TrySetResult(); return Task.CompletedTask; },
            batch: async (_, ct) =>
            {
                batchStarted.TrySetResult();
                await release.Task.WaitAsync(ct);
                Interlocked.Increment(ref batches);
            },
            analyzed: (_, _) =>
            {
                if (Interlocked.Increment(ref analyzed) == 3) analyzedDone.TrySetResult();
                return Task.CompletedTask;
            });

        await PublishAsync("ingestion.batch.test", "batch-1");
        await batchStarted.Task.WaitAsync(Wait);
        Log("batch-1 is in its handler and blocked");
        await PublishAsync("ingestion.batch.test", "batch-2");

        for (var i = 1; i <= 3; i++)
            await PublishAsync("game.analyzed.test", $"analyzed-{i}");
        await PublishAsync("ingestion.test", "legacy-1");

        await analyzedDone.Task.WaitAsync(Wait);
        await legacyDone.Task.WaitAsync(Wait);
        Log("3 game.analyzed and 1 legacy message handled while batch-1 is still blocked");

        Assert.Equal(0, Volatile.Read(ref batches));
        // Prefetch 1: batch-2 waits in the queue, not in the blocked channel.
        Assert.Equal(1u, await MessageCountAsync(_settings.QueueNameV2));
        Log("batch-2 is still ready in its queue (prefetch 1)");

        release.SetResult();
        await WaitUntilAsync(() => Task.FromResult(Volatile.Read(ref batches) == 2), "both batch messages handled");
        Log("released: batch-1 and batch-2 handled");
    }

    [RabbitMqFact]
    public async Task Stopping_mid_message_leaves_it_for_redelivery_not_in_the_dead_letter_queue()
    {
        var batchStarted = NewSignal();
        var service = await StartServiceAsync(
            legacy: Ok,
            batch: async (_, ct) =>
            {
                batchStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
            analyzed: (_, _) => throw new InvalidOperationException("bad payload"));

        // Control: a genuinely bad message does reach the DLQ.
        await PublishAsync("game.analyzed.test", "bad");
        await WaitUntilAsync(async () => await MessageCountAsync(_settings.DeadLetterQueue) == 1, "the bad message in the DLQ");
        Log("control: the failing game.analyzed message is in the DLQ");

        await PublishAsync("ingestion.batch.test", "in-flight");
        await batchStarted.Task.WaitAsync(Wait);
        Log("in-flight batch message is in its handler; stopping the service");
        await service.StopAsync(CancellationToken.None);
        Log("service stopped");

        await WaitUntilAsync(async () => await MessageCountAsync(_settings.QueueNameV2) == 1, "the in-flight message back in its queue");
        Assert.Equal(1u, await MessageCountAsync(_settings.DeadLetterQueue));

        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        var message = await channel.BasicGetAsync(_settings.QueueNameV2, autoAck: true);
        Assert.NotNull(message);
        Assert.Equal("in-flight", Encoding.UTF8.GetString(message.Body.Span));
        Assert.True(message.Redelivered);
        Log("the in-flight message is back in its queue with redelivered=true; the DLQ holds only the control message");
    }

    [RabbitMqFact(restartsBroker: true)]
    public async Task After_a_broker_restart_every_queue_is_consumed_again_without_a_service_restart()
    {
        var seen = new ConcurrentQueue<string>();
        var gate = NewSignal();
        var inFlightStarted = NewSignal();

        await StartServiceAsync(
            legacy: (m, _) => { seen.Enqueue($"legacy:{m}"); return Task.CompletedTask; },
            batch: async (m, _) =>
            {
                if (m == "in-flight")
                {
                    inFlightStarted.TrySetResult();
                    await gate.Task;
                }
                seen.Enqueue($"batch:{m}");
            },
            analyzed: (m, _) => { seen.Enqueue($"analyzed:{m}"); return Task.CompletedTask; });

        await PublishToEveryQueueAsync("before");
        await WaitUntilAsync(() => Task.FromResult(SeenOnEveryQueue(seen, "before")), "'before' on every queue");
        Log("'before' handled on every queue");

        // Mid-flow: a batch message is in its handler when the broker goes down.
        await PublishAsync("ingestion.batch.test", "in-flight");
        await inFlightStarted.Task.WaitAsync(Wait);
        Log("in-flight batch message is in its handler; restarting the broker");

        await RestartBrokerAsync();
        Log("docker restart returned");
        await WaitForConsumersAsync(RecoveryWait);
        Log("all three consumers are back on the broker");
        gate.SetResult();

        await PublishToEveryQueueAsync("after");
        await WaitUntilAsync(() => Task.FromResult(SeenOnEveryQueue(seen, "after")), "'after' on every queue", RecoveryWait);
        Log("'after' handled on every queue");

        // The broker kept the unacked in-flight message and delivered it again after recovery.
        await WaitUntilAsync(() => Task.FromResult(seen.Count(s => s == "batch:in-flight") == 2), "in-flight handled again");
        await WaitUntilAsync(async () => await MessageCountAsync(_settings.QueueNameV2) == 0, "the batch queue drained");
        Assert.Equal(0u, await MessageCountAsync(_settings.DeadLetterQueue));
        Log($"handled in order: {string.Join(", ", seen)}; DLQ empty");
    }

    private static bool SeenOnEveryQueue(ConcurrentQueue<string> seen, string body) =>
        seen.Contains($"legacy:{body}") && seen.Contains($"batch:{body}") && seen.Contains($"analyzed:{body}");

    private async Task PublishToEveryQueueAsync(string body)
    {
        await PublishAsync("ingestion.test", body);
        await PublishAsync("ingestion.batch.test", body);
        await PublishAsync("game.analyzed.test", body);
    }

    private async Task<RabbitMqConsumerService> StartServiceAsync(
        Func<string, CancellationToken, Task> legacy,
        Func<string, CancellationToken, Task> batch,
        Func<string, CancellationToken, Task> analyzed)
    {
        var provider = new ServiceCollection()
            .AddScoped<IIngestionDispatcher>(_ => new DelegateDispatcher(legacy))
            .AddScoped<IBatchIngestionDispatcher>(_ => new DelegateDispatcher(batch))
            .AddScoped<IGameAnalyzedDispatcher>(_ => new DelegateDispatcher(analyzed))
            .BuildServiceProvider();

        var service = new RabbitMqConsumerService(
            provider, Options.Create(_settings), new TestOutputLogger<RabbitMqConsumerService>(output, _clock));
        _services.Add(service);
        await service.StartAsync(CancellationToken.None);
        await WaitForConsumersAsync(Wait);
        return service;
    }

    private static async Task<IConnection> ConnectAsync() =>
        await new ConnectionFactory
        {
            Uri = new Uri(BrokerUri),
            ClientProvidedName = "cato-backend-test-client",
            AutomaticRecoveryEnabled = false
        }.CreateConnectionAsync();

    private async Task PublishAsync(string routingKey, string body)
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        await channel.BasicPublishAsync(
            _settings.ExchangeName, routingKey, mandatory: true,
            new BasicProperties { Persistent = true }, Encoding.UTF8.GetBytes(body));
    }

    private static async Task<uint> MessageCountAsync(string queue)
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        return await channel.MessageCountAsync(queue);
    }

    /// <summary>Waits until each of the three queues has a consumer (it also proves the queues exist).</summary>
    private async Task WaitForConsumersAsync(TimeSpan timeout) =>
        await WaitUntilAsync(async () =>
        {
            try
            {
                await using var connection = await ConnectAsync();
                foreach (var queue in new[] { _settings.QueueName, _settings.QueueNameV2, _settings.GameAnalyzedQueue })
                {
                    await using var channel = await connection.CreateChannelAsync();
                    if (await channel.ConsumerCountAsync(queue) < 1) return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false; // broker still down, or the queue is not declared yet
            }
        }, "a consumer on every queue", timeout);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Wait);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting for {what}.");
            await Task.Delay(250);
        }
    }

    private static async Task RestartBrokerAsync()
    {
        var container = Environment.GetEnvironmentVariable(RabbitMqFactAttribute.ContainerVariable)!;
        using var docker = Process.Start(new ProcessStartInfo("docker", ["restart", container])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        await docker.WaitForExitAsync();
        Assert.True(docker.ExitCode == 0, await docker.StandardError.ReadToEndAsync());
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Log(string message) => output.WriteLine($"{_clock.Elapsed.TotalSeconds,7:F2}s TEST {message}");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var service in _services)
            await service.StopAsync(CancellationToken.None);

        try
        {
            await using var connection = await ConnectAsync();
            await using var channel = await connection.CreateChannelAsync();
            foreach (var queue in new[]
                     {
                         _settings.QueueName, _settings.QueueNameV2, _settings.GameAnalyzedQueue,
                         _settings.DeadLetterQueue
                     })
                await channel.QueueDeleteAsync(queue);
            await channel.ExchangeDeleteAsync(_settings.ExchangeName);
            await channel.ExchangeDeleteAsync(_settings.DeadLetterExchange);
        }
        catch (Exception ex)
        {
            output.WriteLine($"cleanup failed: {ex.Message}");
        }
    }
}

/// <summary>Writes the service's log lines into the test output, with the test's clock.</summary>
internal sealed class TestOutputLogger<T>(ITestOutputHelper output, Stopwatch clock) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var error = exception is null ? "" : $" ({exception.GetType().Name}: {exception.Message})";
        try
        {
            output.WriteLine($"{clock.Elapsed.TotalSeconds,7:F2}s {logLevel} {formatter(state, exception)}{error}");
        }
        catch (InvalidOperationException)
        {
            // A log line after the test finished.
        }
    }
}
