using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record GetSteamDbSnapshotQuery(int AppId, string? Source, int Limit = 100) : IRequest<List<SteamDbSnapshotDto>>;
