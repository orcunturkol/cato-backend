using Cato.API.Services;
using Cato.Infrastructure.Messaging;

namespace Cato.API.Tests.Ingestion;

public class BatchQueuesTests
{
    private static readonly IReadOnlyList<BatchQueues.Route> Routes = BatchQueues.For(new RabbitMqSettings());

    [Fact]
    public void Every_source_the_dispatcher_takes_has_exactly_one_queue()
    {
        // A source without a binding is unroutable: its batches would come back to the
        // collector as failures (mandatory) instead of being stored.
        var routed = Routes.SelectMany(r => r.Sources).ToList();

        Assert.Equal(routed.Count, routed.Distinct().Count());
        Assert.Equal(BatchIngestionDispatcher.Sources.Order(), routed.Order());
    }

    [Fact]
    public void Routes_use_explicit_keys_and_separate_queues()
    {
        Assert.Equal(Routes.Count, Routes.Select(r => r.Queue).Distinct().Count());
        Assert.All(Routes.SelectMany(r => r.Sources), s =>
        {
            Assert.DoesNotContain('*', s);
            Assert.DoesNotContain('#', s);
            Assert.DoesNotContain('.', s);
        });
        Assert.Equal("ingestion.batch.steam_current_players", BatchQueues.RoutingKey("steam_current_players"));
        Assert.Contains(Routes, r => r.Queue == "cato-ingestion-v2" && r.Sources.Contains("steam_financial"));
    }
}
