using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

// Sibling services (OGT) know games by Steam AppId, not by our Guid.
public record EnrichGameByAppIdCommand(int AppId) : IRequest<Result<GameDto>>;
