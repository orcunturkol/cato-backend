using System.Reflection;
using Cato.API.Controllers;
using Cato.API.Models.SteamworksPartners;
using Cato.API.Services.Handlers.SteamworksPartners;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.SteamworksPartners;

/// <summary>
/// The partner registry the collector reads and fills: GET, the discovered upsert and the
/// enable switch, through the controller and the real handlers.
/// </summary>
public class SteamworksPartnersTests : IDisposable
{
    private static readonly DateTime Seeded = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public SteamworksPartnersTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
        // The migration's seed row (EnsureCreated does not run migrations).
        db.SteamworksPartners.Add(new SteamworksPartner
        {
            PartnerId = 216530, Name = "PLAN B YAZILIM OYUN ANONIM SIRKETI", Enabled = true, DiscoveredAt = Seeded,
        });
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private CatoDbContext NewContext() => new(_options);

    private SteamworksPartnersController NewController(CatoDbContext db)
    {
        var mediator = DispatchProxy.Create<IMediator, HandlerMediator>();
        ((HandlerMediator)(object)mediator).Db = db;
        return new SteamworksPartnersController(mediator);
    }

    private async Task<List<SteamworksPartnerDto>> ListAsync()
    {
        await using var db = NewContext();
        var result = await NewController(db).List(default);
        return Assert.IsType<Ok<List<SteamworksPartnerDto>>>(result).Value!;
    }

    private async Task<IResult> DiscoverAsync(params DiscoveredPartner[] partners)
    {
        await using var db = NewContext();
        return await NewController(db).Discovered(new ReportDiscoveredPartnersRequest(partners.ToList()), default);
    }

    [Fact]
    public async Task ListHasTheSeedRow()
    {
        var partner = Assert.Single(await ListAsync());
        Assert.Equal(new SteamworksPartnerDto(216530, "PLAN B YAZILIM OYUN ANONIM SIRKETI", true, Seeded, null), partner);
    }

    [Fact]
    public async Task DiscoveredAddsNewPartnersEnabledAndMarksKnownOnesSeen()
    {
        var result = await DiscoverAsync(
            new DiscoveredPartner(337008, "PLANZET - FZCO"),
            new DiscoveredPartner(216530, "PLAN B YAZILIM"));

        var answer = Assert.IsType<Ok<List<SteamworksPartnerDto>>>(result).Value!;
        Assert.Equal(answer, await ListAsync());
        Assert.Equal([216530, 337008], answer.Select(p => p.PartnerId));

        var planB = answer[0];
        Assert.Equal(("PLAN B YAZILIM", true, Seeded, (DateTime?)Now), (planB.Name, planB.Enabled, planB.DiscoveredAt, planB.LastSeenAt));
        var planzet = answer[1];
        Assert.Equal(("PLANZET - FZCO", true, Now, (DateTime?)Now), (planzet.Name, planzet.Enabled, planzet.DiscoveredAt, planzet.LastSeenAt));
    }

    [Fact]
    public async Task DisabledPartnerStaysDisabledWhenSeenAgain()
    {
        await DiscoverAsync(new DiscoveredPartner(337008, "PLANZET - FZCO"));
        await using (var db = NewContext())
        {
            var result = await NewController(db).Update(337008, new UpdateSteamworksPartnerRequest(false), default);
            Assert.False(Assert.IsType<Ok<SteamworksPartnerDto>>(result).Value!.Enabled);
        }

        await DiscoverAsync(new DiscoveredPartner(337008, "PLANZET - FZCO"));

        Assert.False((await ListAsync()).Single(p => p.PartnerId == 337008).Enabled);
    }

    [Fact]
    public async Task PartnersMissingFromTheListAreKept()
    {
        await DiscoverAsync(new DiscoveredPartner(337008, "PLANZET - FZCO"));

        Assert.Equal([216530, 337008], (await ListAsync()).Select(p => p.PartnerId));
    }

    [Fact]
    public async Task RepeatedIdInOneListIsStoredOnce()
    {
        var result = await DiscoverAsync(
            new DiscoveredPartner(286815, "Old name"),
            new DiscoveredPartner(286815, "Anuta Raul Andrei"));

        var partner = Assert.IsType<Ok<List<SteamworksPartnerDto>>>(result).Value!.Single(p => p.PartnerId == 286815);
        Assert.Equal("Anuta Raul Andrei", partner.Name);
    }

    [Fact]
    public async Task InvalidListIsRefusedAndWritesNothing()
    {
        Assert.IsType<BadRequest<string>>(await DiscoverAsync(new DiscoveredPartner(0, "Zero")));
        Assert.IsType<BadRequest<string>>(await DiscoverAsync(new DiscoveredPartner(337008, " ")));
        await using (var db = NewContext())
            Assert.IsType<BadRequest<string>>(await NewController(db).Discovered(new ReportDiscoveredPartnersRequest(null), default));

        Assert.Single(await ListAsync());
    }

    [Fact]
    public async Task UpdateSwitchesAPartnerAndAnswers404ForAnUnknownOne()
    {
        await using var db = NewContext();
        var controller = NewController(db);

        var off = Assert.IsType<Ok<SteamworksPartnerDto>>(await controller.Update(216530, new UpdateSteamworksPartnerRequest(false), default));
        Assert.False(off.Value!.Enabled);
        var on = Assert.IsType<Ok<SteamworksPartnerDto>>(await controller.Update(216530, new UpdateSteamworksPartnerRequest(true), default));
        Assert.True(on.Value!.Enabled);

        Assert.IsType<NotFound>(await controller.Update(999999, new UpdateSteamworksPartnerRequest(false), default));
        Assert.IsType<BadRequest<string>>(await controller.Update(216530, new UpdateSteamworksPartnerRequest(null), default));
    }

    /// <summary>An <see cref="IMediator"/> that runs the registry's real handlers.</summary>
    public class HandlerMediator : DispatchProxy
    {
        public CatoDbContext Db { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => args?[0] switch
        {
            GetSteamworksPartnersQuery q => new GetSteamworksPartnersHandler(Db).Handle(q, default),
            ReportDiscoveredPartnersCommand c => new ReportDiscoveredPartnersHandler(Db, new FixedTime(Now)).Handle(c, default),
            UpdateSteamworksPartnerCommand c => new UpdateSteamworksPartnerHandler(Db).Handle(c, default),
            _ => throw new NotSupportedException(targetMethod?.Name),
        };
    }

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
