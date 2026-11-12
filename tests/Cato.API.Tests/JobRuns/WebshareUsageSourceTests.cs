using System.Net;
using Cato.API.Services.Alerts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.JobRuns;

public class WebshareUsageSourceTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Handler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            var body = path switch
            {
                "/api/v2/subscription/" =>
                    """{"id":1,"plan":14846336,"start_date":"2026-11-12T09:38:59.167444Z","end_date":"2026-12-12T09:38:59.167444Z"}""",
                "/api/v2/subscription/plan/14846336/" => """{"id":14846336,"status":"active","bandwidth_limit":3.0}""",
                "/api/v2/stats/aggregate/" => """{"bandwidth_total":1073741824,"bandwidth_average":20000}""",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body) });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static WebshareUsageSource Source(Handler handler, string? key = "k3y") =>
        new(new Factory(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [WebshareUsageSource.ApiKeySetting] = key }).Build(),
            NullLogger<WebshareUsageSource>.Instance);

    [Fact]
    public async Task Reads_the_term_the_active_plan_limit_and_the_bytes_since_the_term_start()
    {
        var handler = new Handler();

        var read = await Source(handler).GetAsync(Now, default);

        Assert.False(read.Failed);
        Assert.Equal(new WebshareUsage(1L << 30, 3L << 30,
            new DateTime(2026, 11, 12, 9, 38, 59, 167, DateTimeKind.Utc).AddTicks(4440),
            new DateTime(2026, 12, 12, 9, 38, 59, 167, DateTimeKind.Utc).AddTicks(4440)), read.Usage);
        Assert.All(handler.Requests, r => Assert.Equal("Token k3y", r.Headers.Authorization!.ToString()));
        var stats = handler.Requests[2].RequestUri!.Query;
        Assert.Contains("timestamp__gte=2026-11-12T09%3A38%3A59Z", stats);
        Assert.Contains("timestamp__lte=2026-11-20T12%3A00%3A00Z", stats);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_read()
    {
        var handler = new Handler();

        var read = await Source(handler, key: null).GetAsync(Now, default);

        Assert.Equal(new WebshareUsageRead(null, false), read);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Reads_once_per_max_age_and_keeps_a_failure()
    {
        var handler = new Handler { Status = HttpStatusCode.Unauthorized };
        var source = Source(handler);

        Assert.True((await source.GetAsync(Now, default)).Failed);
        Assert.True((await source.GetAsync(Now.AddMinutes(29), default)).Failed);
        Assert.Single(handler.Requests);

        handler.Status = HttpStatusCode.OK;
        var read = await source.GetAsync(Now.AddMinutes(30), default);
        Assert.False(read.Failed);
        Assert.Equal(4, handler.Requests.Count);
    }
}
