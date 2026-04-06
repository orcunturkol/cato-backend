using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record GetActionQuery(Guid Id) : IRequest<Result<MarketingActionDto>>;
