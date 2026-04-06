using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Users;

/// <summary>
/// Query to list users with pagination support.
/// </summary>
public record ListUsersQuery(
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<UserDto>>;