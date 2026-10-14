using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/games/{appId}")]
[Tags("Game Data")]
public class GameDataController : ControllerBase
{
    private readonly IMediator _mediator;

    public GameDataController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Get peak CCU history for a game.</summary>
    [HttpGet("ccu")]
    [ProducesResponseType(typeof(List<CcuDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetCcuHistory(
        int appId,
        [FromQuery] string? source,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetCcuHistoryQuery(appId, source, limit ?? 100));
        return Results.Ok(result);
    }

    /// <summary>Get financial/sales data for a game.</summary>
    [HttpGet("financials")]
    [ProducesResponseType(typeof(List<FinancialDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetFinancialData(
        int appId,
        [FromQuery] string? countryCode,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetFinancialDataQuery(appId, countryCode, limit ?? 100));
        return Results.Ok(result);
    }

    /// <summary>Get wishlist/traffic data for a game.</summary>
    [HttpGet("traffic")]
    [ProducesResponseType(typeof(List<TrafficDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetTrafficData(
        int appId,
        [FromQuery] string? source,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetTrafficDataQuery(appId, source, limit ?? 100));
        return Results.Ok(result);
    }

    /// <summary>Get owned game wishlist/activation snapshots.</summary>
    [HttpGet("owned-data")]
    [ProducesResponseType(typeof(List<OwnedGameDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetOwnedGameData(
        int appId,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetOwnedGameDataQuery(appId, limit ?? 30));
        return Results.Ok(result);
    }

    /// <summary>Get the daily Steamworks wishlist actions (adds, deletes, purchases, gifts) for an owned game.</summary>
    [HttpGet("steamworks-wishlist/daily")]
    [ProducesResponseType(typeof(List<SteamworksWishlistDailyDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetSteamworksWishlistDaily(
        int appId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await _mediator.Send(new GetSteamworksWishlistDailyQuery(appId, from, to));
        return Results.Ok(result);
    }

    /// <summary>Get the Steamworks wishlist conversions by monthly cohort for an owned game.</summary>
    [HttpGet("steamworks-wishlist/cohorts")]
    [ProducesResponseType(typeof(List<SteamworksWishlistCohortDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetSteamworksWishlistCohorts(
        int appId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await _mediator.Send(new GetSteamworksWishlistCohortsQuery(appId, from, to));
        return Results.Ok(result);
    }

    /// <summary>Get Steamworks wishlist actions by country, summed over a date range, for an owned game.</summary>
    [HttpGet("steamworks-wishlist/countries")]
    [ProducesResponseType(typeof(SteamworksWishlistCountriesDto), StatusCodes.Status200OK)]
    public async Task<IResult> GetSteamworksWishlistCountries(
        int appId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await _mediator.Send(new GetSteamworksWishlistCountriesQuery(appId, from, to));
        return Results.Ok(result);
    }

    /// <summary>Get Steam sales per day (units, gross and net USD) for a game.</summary>
    [HttpGet("sales/daily")]
    [ProducesResponseType(typeof(List<SalesDailyDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetSalesDaily(
        int appId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await _mediator.Send(new GetSalesDailyQuery(appId, from, to));
        return Results.Ok(result);
    }

    /// <summary>Get Steam sales per country, with Steam's region, summed over a date range.</summary>
    [HttpGet("sales/countries")]
    [ProducesResponseType(typeof(SalesCountriesDto), StatusCodes.Status200OK)]
    public async Task<IResult> GetSalesCountries(
        int appId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await _mediator.Send(new GetSalesCountriesQuery(appId, from, to));
        return Results.Ok(result);
    }

    /// <summary>Get Steamworks store traffic totals (impressions, visits, bot and owner traffic) per period.</summary>
    [HttpGet("store-traffic/periods")]
    [ProducesResponseType(typeof(List<StoreTrafficPeriodDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetStoreTrafficPeriods(
        int appId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await _mediator.Send(new GetStoreTrafficPeriodsQuery(appId, from, to));
        return Results.Ok(result);
    }

    /// <summary>Get Steamworks store traffic by page source and by country over a date range.</summary>
    [HttpGet("store-traffic/breakdown")]
    [ProducesResponseType(typeof(StoreTrafficBreakdownDto), StatusCodes.Status200OK)]
    public async Task<IResult> GetStoreTrafficBreakdown(
        int appId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await _mediator.Send(new GetStoreTrafficBreakdownQuery(appId, from, to));
        return Results.Ok(result);
    }

    /// <summary>Get group member count history for a game.</summary>
    [HttpGet("group-members")]
    [ProducesResponseType(typeof(List<GroupMemberCountDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetGroupMemberCount(
        int appId,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetGroupMemberCountQuery(appId, limit ?? 100));
        return Results.Ok(result);
    }

    /// <summary>Get SteamDB snapshot data for a game.</summary>
    [HttpGet("steamdb-snapshots")]
    [ProducesResponseType(typeof(List<SteamDbSnapshotDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetSteamDbSnapshots(
        int appId,
        [FromQuery] string? source,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetSteamDbSnapshotQuery(appId, source, limit ?? 100));
        return Results.Ok(result);
    }
}
