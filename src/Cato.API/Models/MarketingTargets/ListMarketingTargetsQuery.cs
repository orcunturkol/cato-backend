using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.MarketingTargets;

public record ListMarketingTargetsQuery(
    string? TargetType = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<MarketingTargetSummaryDto>>;
