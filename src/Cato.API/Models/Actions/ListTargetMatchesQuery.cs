using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record ListTargetMatchesQuery(
    Guid? GameId = null,
    Guid? TargetId = null,
    string? LifecycleStage = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<TargetMatchDto>>;
