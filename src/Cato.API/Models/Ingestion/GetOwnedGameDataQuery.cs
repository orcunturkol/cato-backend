using MediatR;

namespace Cato.API.Models.Ingestion;

public record GetOwnedGameDataQuery(int AppId, int Limit = 30) : IRequest<List<OwnedGameDto>>;

public record OwnedGameDto(
    Guid Id,
    DateOnly SnapshotDate,
    int WishlistAdditions,
    int WishlistDeletions,
    int PurchasesAndActivations,
    int Gifts,
    int PeriodWishlistBalance);
