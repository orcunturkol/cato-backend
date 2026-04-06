using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.MarketingTargets;

public record CreateMarketingTargetCommand(
    string Name,
    string TargetType,
    string? ContactEmail,
    string? ContactTwitter,
    string? ContactDiscord,
    string? PreferredGenres,
    string? PreferredTags,
    int? AudienceSize,
    string? AudienceRegion,
    string? Platform,
    decimal? EngagementRate,
    decimal? CostEstimateUsd,
    DateOnly? LastContacted,
    decimal? ResponseRate,
    string? Notes
) : IRequest<Result<MarketingTargetDto>>;
