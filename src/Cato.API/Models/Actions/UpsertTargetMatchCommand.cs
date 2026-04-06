using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record UpsertTargetMatchCommand(
    Guid GameId,
    Guid TargetId,
    string? LifecycleStage,
    decimal? RelevanceScore,
    decimal? GenreMatchScore,
    decimal? TagMatchScore,
    decimal? HistoricalPerformanceScore,
    int? SampleSize,
    string? MatchingGenres,
    string? MatchingTags
) : IRequest<Result<TargetMatchDto>>;
