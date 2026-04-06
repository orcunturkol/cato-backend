using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.MarketingTargets;

public record DeleteMarketingTargetCommand(Guid Id) : IRequest<Result<bool>>;
