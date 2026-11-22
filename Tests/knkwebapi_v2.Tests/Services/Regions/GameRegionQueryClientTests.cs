using knkwebapi_v2.Configuration;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Services.Regions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Regions;

/// <summary>
/// PandiO/knk-workspace#988: the region queries to the game server over the game hub, request and answer as gate
/// commands (GateCommandWaiter): the query goes out with a query id, the first answer for that id wins, a timeout, the
/// RegionQueries:Enabled switch and no connected game server give "unavailable" with the reason, never an exception.
/// </summary>
public sealed class GameRegionQueryClientTests
{
    private sealed class RecordingSink : IRealtimeEventSink
    {
        public List<RealtimeEvent> Events { get; } = new();
        public Action<RealtimeEvent>? OnPublish { get; set; }

        public void Publish(RealtimeEvent realtimeEvent)
        {
            Events.Add(realtimeEvent);
            OnPublish?.Invoke(realtimeEvent);
        }
    }

    private readonly RegionQueryOptions _options = new() { Enabled = true, TimeoutSeconds = 1 };
    private readonly RegionQueryWaiter<RegionGeometryResultDto> _geometry = new();
    private readonly RegionQueryWaiter<SpotSiteCheckResultDto> _sites = new();
    private readonly RecordingSink _sink = new();
    private int _servers = 1;

    private GameRegionQueryClient Client()
    {
        var monitor = new Mock<IOptionsMonitor<RegionQueryOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(_options);
        return new GameRegionQueryClient(_geometry, _sites, monitor.Object, () => _servers, _sink);
    }

    private void GameServerAnswersGeometry(Func<RegionQueryPayload, RegionGeometryResultDto> answer) =>
        _sink.OnPublish = e =>
        {
            if (e.Type != RegionQueryEventTypes.RegionGeometryQuery) return;
            Assert.True(_geometry.Complete(answer(Assert.IsType<RegionQueryPayload>(e.Payload))));
        };

    [Fact]
    public void Defaults_AreOn_FiveSeconds()
    {
        Assert.True(new RegionQueryOptions().Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), new RegionQueryOptions().EffectiveTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), new RegionQueryOptions { TimeoutSeconds = 99 }.EffectiveTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), new RegionQueryOptions { TimeoutSeconds = 0 }.EffectiveTimeout);
    }

    [Fact]
    public async Task Geometry_TheAnswerForTheQueryId_IsReturned()
    {
        GameServerAnswersGeometry(p => new RegionGeometryResultDto
        {
            QueryId = p.QueryId, Result = "found", World = "siege", Shape = "Polygon",
            MinX = -3, MinY = 60, MinZ = 2, MaxX = 10, MaxY = 70, MaxZ = 20,
            Points = new List<RegionPointDto> { new() { X = -3, Z = 2 }, new() { X = 10, Z = 2 }, new() { X = 10, Z = 20 } },
        });

        var answer = await Client().GetGeometryAsync(" spot_a ", "siege");

        Assert.Equal(RegionQueryStatus.Found, answer.Status);
        Assert.Equal("polygon", answer.Geometry!.Shape);
        Assert.False(answer.Geometry.IsCuboid);
        Assert.Equal(3, answer.Geometry.Points.Count);
        var sent = Assert.IsType<RegionQueryPayload>(Assert.Single(_sink.Events).Payload);
        Assert.Equal(("spot_a", "siege"), (sent.RegionId, sent.World));
        Assert.Null(Assert.Single(_sink.Events).ServerId); // every connected game server
        Assert.Equal(0, _geometry.PendingCount);
    }

    [Fact]
    public async Task Geometry_NotFound_IsAnAnswer()
    {
        GameServerAnswersGeometry(p => new RegionGeometryResultDto { QueryId = p.QueryId, Result = "not-found" });
        var answer = await Client().GetGeometryAsync("nowhere", null);
        Assert.Equal(RegionQueryStatus.NotFound, answer.Status);
        Assert.Contains("nowhere", answer.Message);
    }

    [Fact]
    public async Task Geometry_ACornerTheWrongWayRound_IsNotUsed()
    {
        GameServerAnswersGeometry(p => new RegionGeometryResultDto { QueryId = p.QueryId, Result = "found", MinX = 5, MaxX = 1 });
        Assert.Equal(RegionQueryStatus.Unavailable, (await Client().GetGeometryAsync("spot_a", null)).Status);
    }

    [Fact]
    public async Task Geometry_AnUnknownOutcome_CountsAsNotChecked()
    {
        GameServerAnswersGeometry(p => new RegionGeometryResultDto { QueryId = p.QueryId, Result = "error", Message = "WorldGuard isn't loaded" });
        var answer = await Client().GetGeometryAsync("spot_a", null);
        Assert.Equal(RegionQueryStatus.Unavailable, answer.Status);
        Assert.Equal("WorldGuard isn't loaded", answer.Message);
    }

    [Fact]
    public async Task NoAnswerInTime_IsATimeout_AndTheWaitIsForgotten()
    {
        var started = DateTime.UtcNow;
        var answer = await Client().GetGeometryAsync("spot_a", null);
        Assert.Equal(RegionQueryStatus.Unavailable, answer.Status);
        Assert.Contains("within 1 s", answer.Message);
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(900));
        Assert.Equal(0, _geometry.PendingCount);
        // A late answer finds nothing waiting.
        var late = (RegionQueryPayload)_sink.Events.Single().Payload!;
        Assert.False(_geometry.Complete(new RegionGeometryResultDto { QueryId = late.QueryId, Result = "found" }));
    }

    [Fact]
    public async Task SwitchedOff_SendsNothing()
    {
        _options.Enabled = false;
        var answer = await Client().CheckSpotSiteAsync("spot_a", null);
        Assert.Equal(RegionQueryStatus.Unavailable, answer.Status);
        Assert.Contains("RegionQueries:Enabled", answer.Message);
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public async Task NoGameServer_SendsNothing()
    {
        _servers = 0;
        var answer = await Client().GetGeometryAsync("spot_a", null);
        Assert.Equal(RegionQueryStatus.Unavailable, answer.Status);
        Assert.Contains("No game server is connected", answer.Message);
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public async Task SiteCheck_TheAnswerIsReturned()
    {
        _sink.OnPublish = e =>
        {
            Assert.Equal(RegionQueryEventTypes.SpotSiteCheckQuery, e.Type);
            var p = Assert.IsType<RegionQueryPayload>(e.Payload);
            Assert.True(_sites.Complete(new SpotSiteCheckResultDto
            {
                QueryId = p.QueryId, Result = "found", FloorBlocks = 63, OpenFloor = 0, VolumeChecked = true, BlockedVolume = 12,
                Warnings = new List<string> { "The volume isn't clear: 12 blocks inside the spot would be replaced by the weapon." },
            }));
        };
        var answer = await Client().CheckSpotSiteAsync("spot_a", "siege");
        Assert.Equal(RegionQueryStatus.Found, answer.Status);
        Assert.True(answer.HasWarnings);
        Assert.Equal(12, answer.BlockedVolume);
    }

    [Fact]
    public void Waiter_FirstAnswerWins_UnknownIdsAreIgnored()
    {
        using var pending = _geometry.Register("q1");
        Assert.True(_geometry.Complete(new RegionGeometryResultDto { QueryId = " q1 ", Result = "found" }));
        Assert.False(_geometry.Complete(new RegionGeometryResultDto { QueryId = "q1", Result = "not-found" }));
        Assert.False(_geometry.Complete(new RegionGeometryResultDto { QueryId = "other" }));
        Assert.False(_geometry.Complete(null));
        Assert.Equal("found", pending.Result.Result.Result);
    }
}
