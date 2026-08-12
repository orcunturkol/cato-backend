namespace Cato.Infrastructure.Messaging;

/// <summary>
/// Handles <c>game.analyzed.*</c> events — a game the reddit_metrics pipeline
/// extracted real wishlist/revenue numbers for. Ensures the game exists in CATO
/// and moves it to the front of the follower-history queue.
/// </summary>
public interface IGameAnalyzedDispatcher
{
    Task DispatchAsync(string messageJson, CancellationToken ct);
}
