using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record GetGameDetailsQuery(Guid Id) : IRequest<Result<GameDto>>;
