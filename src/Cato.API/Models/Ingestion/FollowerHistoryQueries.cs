using Cato.Infrastructure.Redis;
using MediatR;

namespace Cato.API.Models.Ingestion;

// Follower history: SteamDB's follower graph, fetched by the collector's follower job
// from the Redis queue (bands in FollowerHistoryQueue).

/// <summary>
/// Queue a fresh SteamDB fetch of the game's follower history ahead of everything
/// else. Works for any app id: an unknown game is stubbed when its data arrives.
/// </summary>
public record RequestFollowerHistoryCommand(int AppId) : IRequest<FollowerHistoryState>;

public record GetFollowerHistoryStatusQuery(int AppId) : IRequest<FollowerHistoryState>;

/// <summary>
/// A game's daily follower count, oldest first: the SteamDB graph up to its last day,
/// then the daily SteamDB list value, then the Steam community group scrape.
/// </summary>
public record GetFollowerSeriesQuery(int AppId) : IRequest<List<FollowerPointDto>>;

/// <param name="Source">steamdb_follower_history, steamdb_list or steam_community_group.</param>
public record FollowerPointDto(DateOnly Date, int Followers, string Source);
