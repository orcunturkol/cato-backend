using System.Text.Json;
using Cato.API.Health;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Cato.API.Tests.Health;

public class HealthChecksTests
{
    private static readonly HealthCheckContext Context = new();

    [Fact]
    public async Task Database_check_is_healthy_when_the_database_answers()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await using var db = new CatoDbContext(
            new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(connection).Options);

        var result = await new DatabaseHealthCheck(db).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Database_check_is_unhealthy_when_the_database_cannot_open()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "none.db");
        await using var db = new CatoDbContext(new DbContextOptionsBuilder<CatoDbContext>()
            .UseSqlite($"Data Source={missing};Mode=ReadOnly").Options);

        var result = await new DatabaseHealthCheck(db).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Redis_check_is_unhealthy_when_redis_is_not_connected()
    {
        // Port 1 refuses at once; abortConnect=false returns a disconnected multiplexer.
        using var redis = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:1,abortConnect=false,connectTimeout=200");

        var result = await new RedisHealthCheck(redis).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("redis not connected", result.Description);
    }

    [Theory]
    [InlineData(true, HealthStatus.Healthy)]
    [InlineData(false, HealthStatus.Degraded)]
    public async Task SteamKit_logged_out_is_degraded_never_unhealthy(bool loggedIn, HealthStatus expected)
    {
        var result = await new SteamKitHealthCheck(new FakeSteamKit(loggedIn)).CheckHealthAsync(Context);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Json_writer_reports_each_check_without_exception_details()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["database"] = new(HealthStatus.Unhealthy, "database unreachable",
                TimeSpan.FromMilliseconds(12), new InvalidOperationException("secret host name"), null),
            ["steamkit"] = new(HealthStatus.Degraded, "steamkit not logged in", TimeSpan.Zero, null, null),
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(15));
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await HealthEndpoints.WriteJsonAsync(http, report);

        http.Response.Body.Position = 0;
        var text = await new StreamReader(http.Response.Body).ReadToEndAsync();
        using var json = JsonDocument.Parse(text);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("status").GetString());
        var database = json.RootElement.GetProperty("checks").GetProperty("database");
        Assert.Equal("Unhealthy", database.GetProperty("status").GetString());
        Assert.Equal(12, database.GetProperty("durationMs").GetInt64());
        Assert.DoesNotContain("secret host name", text);
        Assert.StartsWith("application/json", http.Response.ContentType);
    }

    private sealed class FakeSteamKit(bool connected) : ISteamKitService
    {
        public bool IsConnected => connected;

        public Task<int?> GetCurrentPlayerCountAsync(int appId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<uint> AppIds, uint LatestChangeNumber)> GetChangedAppIdsSinceAsync(
            uint sinceChangeNumber, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SteamPicsAppInfo?> GetAppInfoAsync(uint appId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SteamPicsRawAppInfo?> GetRawAppInfoAsync(uint appId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SteamPicsRawAppInfo>> GetRawAppInfoBatchAsync(
            IEnumerable<uint> appIds, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
