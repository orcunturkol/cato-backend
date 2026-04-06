using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record GetActionImpactQuery(Guid ActionId) : IRequest<Result<ActionImpactDto>>;
