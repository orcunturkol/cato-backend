using MediatR;

namespace Cato.API.Models.Ingestion;

/// <summary>
/// A game's Steamworks player data: the daily active users / peak concurrent users
/// history (oldest first), the latest players-by-country snapshot (largest share
/// first) and the latest app details of the game and its demo.
/// </summary>
public record GetPlayersQuery(int AppId) : IRequest<PlayersDto>;

public record PlayersDto(
    List<PlayerDayDto> Days,
    DateOnly? CountriesDate,
    List<PlayerCountryDto> Countries,
    List<AppDetailsDto> Details);

// PeakConcurrentUsers is null on rows from the manual upload.
public record PlayerDayDto(DateOnly Date, int Dau, int? PeakConcurrentUsers);

// Region from steam_country; "" when the code is unknown.
public record PlayerCountryDto(string CountryCode, string CountryName, string Region, decimal SharePercent);

public record AppDetailsDto(
    int AppId,
    bool IsDemo,
    DateOnly SnapshotDate,
    int? MedianPlaytimeMinutes,
    string? MedianPlaytimeText,
    long? LifetimeUniqueUsers);
