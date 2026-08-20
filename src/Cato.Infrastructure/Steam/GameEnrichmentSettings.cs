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
    /// Re-enrich games last enriched more than this many days ago. Tags, price and
    /// release dates all drift, but a full refresh is 70k games' worth of Steam
    /// traffic — 0 disables it, which is the default.
    /// </summary>
    public int RefreshAfterDays { get; set; }

    /// <summary>
    /// Whether the sweep continues past the reddit-analysed backlog into games CATO
    /// discovered on its own. Analysed games are always served first either way.
    /// </summary>
    public bool IncludeUnanalyzedGames { get; set; } = true;
}
