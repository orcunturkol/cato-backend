using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

/// <summary>
/// Replaces the list of games whose review totals CATO keeps although it does not sync their
/// reviews in full (OGT sends the games of its closed evaluations once a day).
/// </summary>
public record SetReviewTotalListCommand(IReadOnlyList<int> AppIds) : IRequest<ReviewTotalListDto>;
