using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record ListActionsQuery(
    string? ActionType = null,
    string? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<MarketingActionSummaryDto>>;
