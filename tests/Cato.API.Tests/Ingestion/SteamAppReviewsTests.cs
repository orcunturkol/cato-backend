using System.Net;
using System.Text;
using System.Text.Json;
using Cato.Infrastructure.Steam;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Reviews come from IUserReviewsService/GetAppReviews since Valve switched off the store's
/// appreviews endpoint on 2026-10-22 (it redirects to the store home page now).
/// </summary>
public class SteamAppReviewsTests
{
    // Shape of a live answer (2026-10-23): wrapped in "response", no success field.
    private const string Answer = """
        {"response":{"query_summary":{"num_reviews":1,"review_score":8,"review_score_desc":"Very Positive",
          "total_positive":40,"total_negative":2,"total_reviews":42},
          "reviews":[{"recommendationid":"22736213","author":{"steamid":"76561198000000001","num_reviews":3,
            "playtime_forever":1508,"playtime_last_two_weeks":0,"playtime_at_review":1500,"last_played":1792624435},
            "language":"schinese","review":"good","timestamp_created":1792600000,"timestamp_updated":1792600001,
            "voted_up":true,"votes_up":2,"votes_funny":0,"weighted_vote_score":0.52,"comment_count":0,
            "steam_purchase":true,"received_for_free":false,"written_during_early_access":false,
            "primarily_steam_deck":false,"refunded":false}],
          "cursor":"AoJw8+7b/s6EDeZWhjQc=","total_matching":42}}
        """;

    [Fact]
    public async Task A_page_is_read_from_the_response_wrapper()
    {
        var (api, _) = Api(Answer);

        var page = await api.GetAppReviewsAsync(730);

        Assert.NotNull(page);
        Assert.Equal(42, page.QuerySummary.TotalReviews);
        var review = Assert.Single(page.Reviews);
        Assert.Equal("22736213", review.RecommendationId);
        Assert.Equal("76561198000000001", review.Author.SteamId);
        Assert.True(review.VotedUp);
        Assert.Equal("AoJw8+7b/s6EDeZWhjQc=", page.Cursor);
    }

    [Fact]
    public async Task The_request_asks_for_recent_reviews_in_every_language_through_input_json()
    {
        var (api, handler) = Api(Answer);

        await api.GetAppReviewsAsync(730, "AoJw8+7b/s6EDeZWhjQc=");

        var uri = handler.Request!.RequestUri!;
        Assert.Equal("api.steampowered.com", uri.Host);
        Assert.Equal("/IUserReviewsService/GetAppReviews/v1/", uri.AbsolutePath);
        var raw = uri.Query.TrimStart('?');
        Assert.StartsWith("input_json=", raw);
        using var input = JsonDocument.Parse(Uri.UnescapeDataString(raw["input_json=".Length..]));
        Assert.Equal(730, input.RootElement.GetProperty("appid").GetInt32());
        Assert.Equal(1, input.RootElement.GetProperty("filter").GetInt32());
        Assert.Equal(["all"], input.RootElement.GetProperty("languages").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal(100, input.RootElement.GetProperty("num_per_page").GetInt32());
        Assert.Equal("AoJw8+7b/s6EDeZWhjQc=", input.RootElement.GetProperty("cursor").GetString());
    }

    [Fact]
    public async Task An_http_error_is_null()
    {
        var (api, _) = Api("{}", HttpStatusCode.Forbidden);

        Assert.Null(await api.GetAppReviewsAsync(730));
    }

    [Fact]
    public async Task A_review_read_does_not_wait_for_a_store_read()
    {
        var handler = new SlowStoreHandler(Answer);
        var api = new SteamApiService(new HttpClient(handler), NullLogger<SteamApiService>.Instance,
            Options.Create(new SteamWebApiSettings()));
        var store = api.GetAppDetailsAsync(730);
        await handler.StoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The store read holds the store throttle until the gate opens.
        var page = await api.GetAppReviewsAsync(730).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(page);
        handler.StoreGate.SetResult();
        await store;
    }

    private static (SteamApiService Api, FakeHandler Handler) Api(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(status, body);
        var api = new SteamApiService(new HttpClient(handler), NullLogger<SteamApiService>.Instance,
            Options.Create(new SteamWebApiSettings()));
        return (api, handler);
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>Store requests wait for <see cref="StoreGate"/>; Web API requests answer at once.</summary>
    private sealed class SlowStoreHandler(string reviewsBody) : HttpMessageHandler
    {
        public TaskCompletionSource StoreStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StoreGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = reviewsBody;
            if (request.RequestUri!.Host == "store.steampowered.com")
            {
                StoreStarted.TrySetResult();
                await StoreGate.Task.WaitAsync(ct);
                body = "{}";
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
