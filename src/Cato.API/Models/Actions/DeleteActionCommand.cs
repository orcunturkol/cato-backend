using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record DeleteActionCommand(Guid Id) : IRequest<Result<bool>>;
