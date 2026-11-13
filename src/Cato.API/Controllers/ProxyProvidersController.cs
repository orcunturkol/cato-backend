using Cato.API.Services.Proxies;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/proxy-providers")]
[Tags("Proxy Providers")]
public class ProxyProvidersController(ProxyProvidersService service) : ControllerBase
{
    /// <summary>
    /// Per proxy provider: success and failure counts of the collector's requests over the last
    /// <paramref name="days"/> UTC days (1-40, default 7), failure reasons, targets, down flag
    /// and usage from the provider's API; plus the fallback in use, if any.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ProxyProvidersDto), StatusCodes.Status200OK)]
    public async Task<IResult> Get([FromQuery] int days = 7, CancellationToken ct = default) =>
        Results.Ok(await service.GetAsync(days, DateTime.UtcNow, ct));
}
