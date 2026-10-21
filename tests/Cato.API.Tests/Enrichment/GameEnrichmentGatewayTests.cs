using System.Net;
using Cato.API.Controllers;
using Cato.API.Hosting;
using Cato.API.Services.Enrichment;
using Cato.Infrastructure.Steam;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.Enrichment;

public class GameEnrichmentGatewayTests
{
    private static readonly Guid GameId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static (RemoteGameEnrichmentGateway Gateway, FakeHandler Handler) Remote(HttpStatusCode status)
    {
        var handler = new FakeHandler(status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://worker:8080/") };
        return (new RemoteGameEnrichmentGateway(http, NullLogger<RemoteGameEnrichmentGateway>.Instance), handler);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, EnrichmentOutcome.Enriched)]
    [InlineData(HttpStatusCode.NotFound, EnrichmentOutcome.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, EnrichmentOutcome.Failed)]
    public async Task The_api_role_asks_the_worker_and_maps_its_answer(HttpStatusCode status, EnrichmentOutcome expected)
    {
        var (gateway, handler) = Remote(status);

        Assert.Equal(expected, await gateway.EnrichAsync(GameId, default));
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal($"http://worker:8080/api/internal/enrichment/games/{GameId}", handler.Request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_worker_that_is_down_or_failing_is_an_error_not_a_quiet_failure(HttpStatusCode status)
    {
        var (gateway, _) = Remote(status);

        await Assert.ThrowsAsync<HttpRequestException>(() => gateway.EnrichAsync(GameId, default));
    }

    [Theory]
    [InlineData(true, EnrichmentOutcome.Enriched)]
    [InlineData(false, EnrichmentOutcome.Failed)]
    public async Task The_worker_enriches_locally(bool success, EnrichmentOutcome expected)
    {
        var gateway = new LocalGameEnrichmentGateway(new FakeEnrichment(success));

        Assert.Equal(expected, await gateway.EnrichAsync(GameId, default));
    }

    [Theory]
    [InlineData(true, StatusCodes.Status200OK)]
    [InlineData(false, StatusCodes.Status400BadRequest)]
    public async Task The_internal_endpoint_keeps_the_status_contract(bool success, int status)
    {
        var controller = new InternalEnrichmentController(new LocalGameEnrichmentGateway(new FakeEnrichment(success)));

        var result = await controller.Enrich(GameId, default);

        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public void Only_the_worker_serves_enrichment_and_steamkit()
    {
        foreach (var controller in new[] { typeof(InternalEnrichmentController), typeof(SteamKitController) })
        {
            Assert.False(CatoRole.Api.MapsController(controller));
            Assert.True(CatoRole.Worker.MapsController(controller));
            Assert.True(CatoRole.All.MapsController(controller));
        }
        Assert.True(CatoRole.Api.MapsController(typeof(GamesController)));
    }

    private sealed class FakeHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class FakeEnrichment(bool success) : ISteamGameEnrichmentService
    {
        public Task<bool> EnrichGameAsync(Guid gameId, CancellationToken ct = default) => Task.FromResult(success);
    }
}
