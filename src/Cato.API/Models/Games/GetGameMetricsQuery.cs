using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

/// <summary>
/// Follower series, latest SteamDB list ranks and recent CCU for many games at once
/// (OGT's evaluation list). Unknown AppIds are left out of the result.
/// </summary>
public record GetGameMetricsQuery(IReadOnlyList<int> AppIds, int Days = 30) : IRequest<List<GameMetricsDto>>;
