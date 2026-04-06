using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Users;

/// <summary>
/// Query to retrieve a user by ID.
/// </summary>
public record GetUserQuery(Guid Id) : IRequest<Result<UserDto>>;