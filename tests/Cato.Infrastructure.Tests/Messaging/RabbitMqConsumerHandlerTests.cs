using System.Reflection;
using Cato.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Target = Cato.Infrastructure.Messaging.RabbitMqConsumerService.DispatchTarget;

namespace Cato.Infrastructure.Tests.Messaging;

/// <summary>What the handler does with the delivery after each kind of dispatch outcome.</summary>
public class RabbitMqConsumerHandlerTests
{
    /// <summary>Records ack and nack; the handler uses no other channel member.</summary>
    public class RecordingChannel : DispatchProxy
    {
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var token = (CancellationToken)args![^1]!;
            Calls.Add(method!.Name switch
            {
                nameof(IChannel.BasicAckAsync) => $"ack cancelled={token.IsCancellationRequested}",
                nameof(IChannel.BasicNackAsync) => $"nack requeue={args[2]} cancelled={token.IsCancellationRequested}",
                _ => throw new NotSupportedException(method.Name)
            });
            return default(ValueTask);
        }
    }

    private static async Task<List<string>> HandleAsync(
        Func<CancellationToken, Task> dispatch, CancellationToken stoppingToken)
    {
        var services = new ServiceCollection()
            .AddScoped<IBatchIngestionDispatcher>(_ => new DelegateDispatcher((_, ct) => dispatch(ct)))
            .BuildServiceProvider();
        var service = new RabbitMqConsumerService(
            services, Options.Create(new RabbitMqSettings()), NullLogger<RabbitMqConsumerService>.Instance);

        var channel = DispatchProxy.Create<IChannel, RecordingChannel>();
        var delivery = new BasicDeliverEventArgs(
            "consumer", 7, false, "cato-data", "ingestion.batch.test",
            new BasicProperties(), "{}"u8.ToArray());

        await service.HandleAsync(channel, delivery, Target.Batch, stoppingToken);
        return ((RecordingChannel)(object)channel).Calls;
    }

    [Fact]
    public async Task A_handled_message_is_acked()
    {
        var calls = await HandleAsync(_ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(["ack cancelled=False"], calls);
    }

    [Fact]
    public async Task A_failing_message_is_nacked_to_the_dead_letter_queue()
    {
        var calls = await HandleAsync(
            _ => throw new InvalidOperationException("bad payload"), CancellationToken.None);

        Assert.Equal(["nack requeue=False cancelled=False"], calls);
    }

    [Fact]
    public async Task A_message_cut_short_by_shutdown_is_neither_acked_nor_nacked()
    {
        using var stopping = new CancellationTokenSource();

        var calls = await HandleAsync(async ct =>
        {
            await stopping.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
        }, stopping.Token);

        Assert.Empty(calls);
    }

    [Fact]
    public async Task A_timeout_outside_shutdown_still_goes_to_the_dead_letter_queue()
    {
        // HttpClient reports a timeout as TaskCanceledException; without a shutdown it is a failure.
        var calls = await HandleAsync(
            _ => throw new TaskCanceledException("request timed out"), CancellationToken.None);

        Assert.Equal(["nack requeue=False cancelled=False"], calls);
    }

    [Fact]
    public async Task A_message_finished_during_shutdown_is_still_acked()
    {
        using var stopping = new CancellationTokenSource();

        var calls = await HandleAsync(_ => stopping.CancelAsync(), stopping.Token);

        Assert.Equal(["ack cancelled=False"], calls);
    }
}

/// <summary>A dispatcher for every queue that runs the given delegate.</summary>
internal sealed class DelegateDispatcher(Func<string, CancellationToken, Task> dispatch)
    : IIngestionDispatcher, IBatchIngestionDispatcher, IGameAnalyzedDispatcher
{
    public Task DispatchAsync(string messageJson, CancellationToken ct) => dispatch(messageJson, ct);
}
