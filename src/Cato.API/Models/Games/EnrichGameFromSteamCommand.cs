using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record EnrichGameFromSteamCommand(Guid Id) : IRequest<Result<GameDto>>;
