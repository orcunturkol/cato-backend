using MediatR;

namespace Cato.API.Models.Ingestion;

public record GetTrafficDataQuery(int AppId, string? Source, int Limit = 100) : IRequest<List<TrafficDto>>;

public record TrafficDto(
    Guid Id,
    DateOnly TrafficDate,
    int WishlistAdditions,
    int WishlistDeletions,
    int NetWishlistChange,
    int Purchases,
    string? TrafficSource,
    decimal? PurchaseConversionRate);
