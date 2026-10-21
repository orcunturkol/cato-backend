namespace Cato.Infrastructure.Messaging;

/// <summary>
/// Which queue each batch source goes to (routing key <c>ingestion.batch.&lt;source&gt;</c>).
/// Each queue has its own consumer channel, so a slow source holds up only its own queue:
/// the full-history Steamworks payloads no longer delay CCU. The keys are explicit, without
/// a wildcard. A source missing here has no route; the collector publishes with
/// mandatory=True, so such a batch fails (and alerts) instead of being dropped.
/// </summary>
public static class BatchQueues
{
    public sealed record Route(string Queue, IReadOnlyList<string> Sources);

    /// <summary>The single binding of every batch source before 2026-10; removed at start.</summary>
    public const string LegacyWildcard = "ingestion.batch.#";

    public static string RoutingKey(string source) => $"ingestion.batch.{source}";

    public static IReadOnlyList<Route> For(RabbitMqSettings settings) =>
    [
        new(settings.QueueNameCcu, ["steam_current_players"]),
        new(settings.QueueNameFollowers, ["steamdb_follower_history"]),
        new(settings.QueueNameSteamworks,
        [
            "steamworks_wishlist_report", "steamworks_store_traffic", "steamworks_nav_stats",
            "steamworks_players", "steamworks_demo_downloads", "steamworks_bundle_sales", "steamworks_nextfest",
        ]),
        new(settings.QueueNameV2, ["group_member_count", "steamdb_most_wished", "steamdb_wishlist_activity", "steam_financial"]),
    ];
}
