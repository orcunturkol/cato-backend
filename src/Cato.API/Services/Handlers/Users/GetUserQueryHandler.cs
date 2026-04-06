using Cato.API.DTOs;
using Cato.API.Models.Users;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Users;

/// <summary>
/// Handler for retrieving a user by ID using the GetUserQuery.
/// </summary>
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, Result<UserDto>>
{
    private readonly CatoDbContext _context;

    public GetUserQueryHandler(CatoDbContext context)
    {
        _context = context;
    }

    public async Task<Result<UserDto>> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
            
        if (user == null)
        {
            return Result<UserDto>.Failure("User not found.");
        }

        return Result<UserDto>.Success(new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        });
    }
}