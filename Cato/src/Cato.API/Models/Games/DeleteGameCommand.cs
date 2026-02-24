using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record DeleteGameCommand(Guid Id) : IRequest<Result<bool>>;
