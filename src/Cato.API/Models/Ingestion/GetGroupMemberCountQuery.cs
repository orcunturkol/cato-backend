using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record GetGroupMemberCountQuery(int AppId, int Limit = 100) : IRequest<List<GroupMemberCountDto>>;
