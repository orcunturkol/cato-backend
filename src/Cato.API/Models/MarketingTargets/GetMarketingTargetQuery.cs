using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.MarketingTargets;

public record GetMarketingTargetQuery(Guid Id) : IRequest<Result<MarketingTargetDto>>;
