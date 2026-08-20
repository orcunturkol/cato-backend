namespace Cato.Infrastructure.Steam;

/// <summary>Settings for the background sweep that fills in missing Steam store data.</summary>
public class GameEnrichmentSettings
{
    public const string SectionName = "GameEnrichment";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Games enriched per cycle. Each one costs two throttled Steam calls (~3s), and
    /// the throttle is shared with every other Steam caller in the process, so a
    /// batch of 200 occupies roughly ten minutes of an hour-long cycle.
    /// </summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>Consecutive failures after which a game is left alone.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Re-enrich games last enriched more than this many days ago; 0 disables it.
    /// Tags, price and release dates all drift, and a full pass is ~62k games' worth
    /// of Steam traffic, so it runs behind everything that has no store record at all
    /// — except for reddit-analysed games, which are refreshed before either general
    /// band.
    ///
    /// Do not use this to repair records written by an older version of the
    /// enrichment code. It keys off <c>LastEnrichedAt</c>, which was seeded from
    /// <c>UpdatedAt</c> for pre-existing rows and therefore reads "last touched by
    /// anything"; a 30-day window found 426 of 721 known-stale games. Requeue those
    /// explicitly instead — see the RequeueStaleAnalyzedGamesForEnrichment migration.
    /// </summary>
    public int RefreshAfterDays { get; set; }

    /// <summary>
    /// Whether the sweep continues past the reddit-analysed backlog into games CATO
    /// discovered on its own. Analysed games are always served first either way.
    /// </summary>
    public bool IncludeUnanalyzedGames { get; set; } = true;
}
