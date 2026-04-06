using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Users;

/// <summary>
/// Command to create a new user in the system.
/// </summary>
public record CreateUserCommand(
    string Username,
    string Email,
    string FirstName,
    string LastName
) : IRequest<Result<UserDto>>;