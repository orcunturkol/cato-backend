using Cato.API.Services.Credentials;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

public record UpdateSteamworksRefreshRequest(string? Cookie, string? ChangedBy);

/// <summary>
/// Logins of the collector's jobs. Write-only: the status shows the masked account and the
/// token dates; no endpoint ever returns a cookie. Behind the Caddy password for non-GET.
/// </summary>
[ApiController]
[Route("api/credentials")]
[Tags("Job Runs")]
public class CredentialsController : ControllerBase
{
    private readonly SteamworksLoginService _steamworks;

    public CredentialsController(SteamworksLoginService steamworks) => _steamworks = steamworks;

    [HttpGet("steamworks")]
    [ProducesResponseType(typeof(SteamworksLoginStatus), StatusCodes.Status200OK)]
    public Task<SteamworksLoginStatus> Steamworks(CancellationToken ct) => _steamworks.GetStatusAsync(ct);

    /// <summary>
    /// Stores a new steamRefresh_partner cookie: the value, or the cookie header of the
    /// login.steampowered.com/jwt/refresh request (DevTools → Headers → cookie → Copy value).
    /// </summary>
    [HttpPut("steamworks/refresh-cookie")]
    [ProducesResponseType(typeof(SteamworksLoginUpdate), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> UpdateSteamworksRefresh([FromBody] UpdateSteamworksRefreshRequest request, CancellationToken ct)
    {
        var result = await _steamworks.UpdateRefreshAsync(request.Cookie, request.ChangedBy, ct);
        return result.IsSuccess ? Results.Ok(result.Data) : Results.BadRequest(new { error = result.ErrorMessage });
    }
}
