using Cato.API.Services;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/steamkit")]
public class SteamKitController : ControllerBase
{
    private readonly ISteamKitDataService _steamKitData;
    private readonly ISteamKitService _steamKit;

    public SteamKitController(ISteamKitDataService steamKitData, ISteamKitService steamKit)
    {
        _steamKitData = steamKitData;
        _steamKit = steamKit;
    }

    /// <summary>Get the live concurrent player count for a Steam AppId.</summary>
    [HttpGet("ccu/{appId:int}")]
    public async Task<IActionResult> GetLiveCcu(int appId, CancellationToken ct)
    {
        var ccu = await _steamKitData.GetLiveCcuAsync(appId, ct);
        if (ccu is null)
            return StatusCode(503, new { Error = "Could not retrieve CCU. SteamKit2 may not be connected." });

        return Ok(new { AppId = appId, CurrentPlayers = ccu });
    }

    /// <summary>Fetch live CCU for an AppId and persist it to CcuHistory.</summary>
    [HttpPost("ccu/{appId:int}/save")]
    public async Task<IActionResult> SaveLiveCcu(int appId, CancellationToken ct)
    {
        var ccu = await _steamKitData.SaveLiveCcuAsync(appId, ct);
        if (ccu is null)
            return StatusCode(503, new { Error = "Could not retrieve or save CCU." });

        return Ok(new { AppId = appId, CurrentPlayers = ccu, Saved = true });
    }

    /// <summary>
    /// Fetch raw PICS changes since a given change number.
    /// Useful for debugging and admin inspection.
    /// </summary>
    [HttpGet("changes")]
    public async Task<IActionResult> GetChanges([FromQuery] uint since = 0, CancellationToken ct = default)
    {
        var (appIds, latestChangeNumber) = await _steamKit.GetChangedAppIdsSinceAsync(since, ct);

        return Ok(new
        {
            SinceChangeNumber = since,
            LatestChangeNumber = latestChangeNumber,
            ChangedAppCount = appIds.Count,
            AppIds = appIds
        });
    }

    /// <summary>Get PICS metadata for a specific AppId.</summary>
    [HttpGet("appinfo/{appId:int}")]
    public async Task<IActionResult> GetAppInfo(int appId, CancellationToken ct)
    {
        var info = await _steamKit.GetAppInfoAsync((uint)appId, ct);
        if (info is null)
            return NotFound(new { Error = $"No PICS data found for AppId {appId}" });

        return Ok(info);
    }

    /// <summary>Returns current SteamKit2 connection status.</summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new { IsConnected = _steamKit.IsConnected });
    }
}
