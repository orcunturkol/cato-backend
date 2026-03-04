using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record UpdateGameCommand(
    Guid Id,
    string? Name,
    string? GameType,
    decimal? PriceUsd,
    bool? IsEarlyAccess,
    bool? IsReleased,
    string? ShortDescription,
    string? Website,
    string? SteamReviewScore,
    int? ReviewCount,
    int? FollowersCount
) : IRequest<Result<GameDto>>;
