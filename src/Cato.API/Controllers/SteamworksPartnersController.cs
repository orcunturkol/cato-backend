using Cato.API.Models.SteamworksPartners;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

/// <summary>
/// The Steamworks partners (organizations) the collector fetches. The collector reports the
/// partners its login sees; a person can switch one off.
/// </summary>
[ApiController]
[Route("api/steamworks-partners")]
[Tags("Steamworks")]
public class SteamworksPartnersController : ControllerBase
{
    private readonly IMediator _mediator;

    public SteamworksPartnersController(IMediator mediator) => _mediator = mediator;

    /// <summary>Every known partner, by partner id.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<SteamworksPartnerDto>), StatusCodes.Status200OK)]
    public async Task<IResult> List(CancellationToken ct) =>
        Results.Ok(await _mediator.Send(new GetSteamworksPartnersQuery(), ct));

    /// <summary>
    /// The collector's "View as" list: adds new partners (enabled), renames known ones and sets
    /// their LastSeenAt. A disabled partner stays disabled. Answers with every partner.
    /// </summary>
    [HttpPost("discovered")]
    [ProducesResponseType(typeof(List<SteamworksPartnerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> Discovered([FromBody] ReportDiscoveredPartnersRequest request, CancellationToken ct)
    {
        if (request.Partners is null)
            return Results.BadRequest("partners is required.");
        if (request.Partners.Any(p => p is null || p.PartnerId <= 0 || string.IsNullOrWhiteSpace(p.Name)))
            return Results.BadRequest("Each partner needs a positive partnerId and a name.");

        // The last copy of a repeated id wins.
        var partners = request.Partners.GroupBy(p => p.PartnerId).Select(g => g.Last()).ToList();
        return Results.Ok(await _mediator.Send(new ReportDiscoveredPartnersCommand(partners), ct));
    }

    /// <summary>Switch the fetch of one partner on or off.</summary>
    [HttpPut("{partnerId:int}")]
    [ProducesResponseType(typeof(SteamworksPartnerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> Update(int partnerId, [FromBody] UpdateSteamworksPartnerRequest request, CancellationToken ct)
    {
        if (request.Enabled is not { } enabled)
            return Results.BadRequest("enabled is required.");
        var partner = await _mediator.Send(new UpdateSteamworksPartnerCommand(partnerId, enabled), ct);
        return partner is null ? Results.NotFound() : Results.Ok(partner);
    }

    /// <summary>
    /// Replace the apps the partner lists on partner.steampowered.com (the collector's
    /// wishlist listing). An empty list clears them. Answers with the stored app ids.
    /// </summary>
    [HttpPut("{partnerId:int}/apps")]
    [ProducesResponseType(typeof(List<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> ReplaceApps(int partnerId, [FromBody] ReplaceSteamworksPartnerAppsRequest request, CancellationToken ct)
    {
        if (request.AppIds is null)
            return Results.BadRequest("appIds is required.");
        if (request.AppIds.Any(id => id <= 0))
            return Results.BadRequest("Each app id must be positive.");

        var stored = await _mediator.Send(new ReplaceSteamworksPartnerAppsCommand(partnerId, request.AppIds), ct);
        return stored is null ? Results.NotFound() : Results.Ok(stored);
    }
}
