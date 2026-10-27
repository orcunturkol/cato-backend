namespace Cato.Domain.Entities;

/// <summary>The values of <see cref="Game.GameType"/>.</summary>
public static class GameTypes
{
    public const string Owned = "Owned";
    public const string Sourcing = "Sourcing";
    public const string Competitor = "Competitor";
    public const string Other = "Other";

    /// <summary>
    /// A game the OGT team is evaluating. OGT sets it while the evaluation is open and
    /// puts the earlier type back when it closes; an Owned game keeps its type.
    /// </summary>
    public const string Ogt = "OGT";

    /// <summary>
    /// Types whose games the collectors follow: reviews, CCU, the community group scrape,
    /// achievements. One list, so a new type cannot be left out of one collector.
    /// </summary>
    public static readonly string[] Tracked = [Sourcing, Owned, Ogt];

    public static bool IsTracked(string gameType) => Tracked.Contains(gameType);
}
