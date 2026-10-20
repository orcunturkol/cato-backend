using MediatR;

namespace Cato.API.Models.Ingestion;

/// <summary>Every Next Fest the publisher's games took part in, newest first, with each game's final and daily numbers.</summary>
public record GetNextFestQuery : IRequest<NextFestDto>;

public record NextFestDto(List<NextFestDto.Fest> Fests)
{
    public record Fest(string FestKey, string Slug, DateTime? EventStart, DateTime? EventEnd, List<Game> Games);

    // The last snapshot's cumulative numbers; Days holds every snapshot, oldest first.
    public record Game(int AppId, Guid GameId, string? Name, int? DemoAppId, int DemoPlayers, int Wishlists, int PlayerWishlists,
        DateTime LastSnapshot, List<Day> Days);

    public record Day(DateTime SnapshotEnd, int DemoPlayers, int Wishlists, int PlayerWishlists);
}
