using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Road navigation rev. 6 Part B (docs/specs/navigation/IMPLEMENTATION_PLAN.md §5.7): tile state
/// (D1), confirmed edges (D4) and stored proposals (D5), against the real repository on EF InMemory.
/// </summary>
public class RoadCuratedTilesTests : IDisposable
{
    private const string World = "world";
    private readonly KnKDbContext _db;
    private readonly RoadNetworkService _service;

    public RoadCuratedTilesTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _db.RoadProfiles.Add(new RoadProfile { Id = 1, Name = "Default road", RoadClass = RoadClass.Road });
        _db.SaveChanges();
        _service = new RoadNetworkService(new RoadNetworkRepository(_db), RoadNetworkServiceTests.Mapper());
    }

    public void Dispose() => _db.Dispose();

    private static int[] P(int x, int y, int z) => new[] { x, y, z };

    private static RoadTileGraphNodeDto Node(string key, int x, int z, RoadNodeKind kind, int? existingId = null) =>
        new() { Key = key, X = x, Y = 64, Z = z, Kind = kind, ExistingId = existingId };

    private static RoadTileGraphEdgeDto Edge(string from, string to, int[][] geometry) =>
        new()
        {
            FromKey = from, ToKey = to, Geometry = geometry, ProfileId = 1, AvgWidth = 3,
            Length = knkwebapi_v2.Services.Roads.RoadGeometry.PolylineLength(geometry)
        };

    /// <summary>Junction J at x=200 with a spur to endpoint E at x=300 (z=100, tile 0,0); with
    /// <paramref name="spur"/> false the build lost the spur.</summary>
    private static RoadTileGraphUpsertDto Build(bool spur = true)
    {
        var dto = new RoadTileGraphUpsertDto
        {
            BuilderVersion = 5, CellCount = 100, LevelCount = 1,
            Nodes = { Node("b", 0, 100, RoadNodeKind.Boundary), Node("j", 200, 100, RoadNodeKind.Junction) },
            Edges = { Edge("b", "j", new[] { P(0, 64, 100), P(200, 64, 100) }) }
        };
        if (spur)
        {
            dto.Nodes.Add(Node("e", 300, 100, RoadNodeKind.Endpoint));
            dto.Edges.Add(Edge("j", "e", new[] { P(200, 64, 100), P(300, 64, 100) }));
        }
        return dto;
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static RoadTileProposalUpsertDto Proposal(string items = "[{\"n\":1,\"kind\":\"EDGE_ADDED\"}]", string rejected = "[]") => new()
    {
        BaseVersion = 1, BuilderVersion = 5, CreatedBy = "Pandi",
        Items = Json(items), Rejected = Json(rejected), AddedCount = 1
    };

    private async Task<RoadTileGraphDto> GraphAsync() => (await _service.GetTileGraphAsync(World, 0, 0))!;

    // ------------------------------------------------------------- State (D1)

    [Fact]
    public async Task FirstUpsert_CuratesTheTile()
    {
        var result = await _service.UpsertTileGraphAsync(World, 0, 0, Build());

        Assert.Equal(RoadTileState.Curated, result.Tile.State);
        Assert.NotNull(result.Tile.CuratedAt);
        Assert.Equal(RoadTileState.Curated, (await GraphAsync()).Tile.State);
    }

    [Fact]
    public async Task DirtyMark_LeavesANewTileDetected()
    {
        var tile = await _service.MarkTileDirtyAsync(World, 3, 3);

        Assert.Equal(RoadTileState.Detected, tile.State);
        Assert.Null(tile.CuratedAt);
    }

    [Fact]
    public async Task Uncurate_IsOneShot_TheNextUpsertCuratesAgain_AndTheVersionStays()
    {
        var first = await _service.UpsertTileGraphAsync(World, 0, 0, Build());
        var curatedAt = first.Tile.CuratedAt;

        var detected = await _service.SetTileStateAsync(World, 0, 0, new RoadTileStateDto { State = RoadTileState.Detected });
        Assert.Equal((RoadTileState.Detected, first.Tile.Version, curatedAt), (detected.State, detected.Version, detected.CuratedAt));

        var second = await _service.UpsertTileGraphAsync(World, 0, 0, Build());
        Assert.Equal(RoadTileState.Curated, second.Tile.State);
        Assert.Equal(curatedAt, second.Tile.CuratedAt); // the first curation time is kept
    }

    [Fact]
    public async Task Curate_SetsCuratedAtOnce()
    {
        await _service.MarkTileDirtyAsync(World, 0, 0);

        var curated = await _service.SetTileStateAsync(World, 0, 0, new RoadTileStateDto { State = RoadTileState.Curated });
        var again = await _service.SetTileStateAsync(World, 0, 0, new RoadTileStateDto { State = RoadTileState.Curated });

        Assert.Equal(RoadTileState.Curated, curated.State);
        Assert.NotNull(curated.CuratedAt);
        Assert.Equal(curated.CuratedAt, again.CuratedAt);
    }

    [Fact]
    public async Task SetState_UnknownTileOrState_IsRefused()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.SetTileStateAsync(World, 9, 9, new RoadTileStateDto { State = RoadTileState.Curated }));
        await _service.MarkTileDirtyAsync(World, 0, 0);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SetTileStateAsync(World, 0, 0, new RoadTileStateDto { State = (RoadTileState)7 }));
    }

    // ------------------------------------------------------- Confirmed edges (D4)

    [Fact]
    public async Task ConfirmedEdge_SurvivesABuildThatLostIt_WithItsNodes()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Build());
        var spur = (await GraphAsync()).Edges.Single(e => e.Geometry.Any(p => p[0] == 300));

        var updated = await _service.UpdateEdgeAsync(spur.Id, new RoadEdgeUpdateDto { Confirmed = true });
        Assert.True(updated.Edge.Confirmed);
        Assert.All((await GraphAsync()).Nodes.Where(n => n.Id == spur.FromNodeId || n.Id == spur.ToNodeId), n => Assert.True(n.Locked));

        var graph = await GraphAsync();
        var ids = graph.Nodes.ToDictionary(n => n.X, n => n.Id);
        var rebuild = Build(spur: false);
        rebuild.Nodes[0].ExistingId = ids[0];
        rebuild.Nodes[1].ExistingId = ids[200];
        await _service.UpsertTileGraphAsync(World, 0, 0, rebuild);

        var after = await GraphAsync();
        Assert.Contains(after.Edges, e => e.Id == spur.Id && e.Confirmed);
        Assert.Contains(after.Nodes, n => n.X == 300);
    }

    [Fact]
    public async Task UnconfirmedEdge_GoesWithTheNextBuildThatLostIt()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Build());
        var spur = (await GraphAsync()).Edges.Single(e => e.Geometry.Any(p => p[0] == 300));
        await _service.UpdateEdgeAsync(spur.Id, new RoadEdgeUpdateDto { Confirmed = true });

        var cleared = await _service.UpdateEdgeAsync(spur.Id, new RoadEdgeUpdateDto { Confirmed = false });
        Assert.False(cleared.Edge.Confirmed);
        await _service.UpsertTileGraphAsync(World, 0, 0, Build(spur: false));

        Assert.DoesNotContain((await GraphAsync()).Edges, e => e.Id == spur.Id);
    }

    [Fact]
    public async Task Confirm_IsRefusedOnARecordedEdge()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Build());
        var recorded = await _service.CreateRecordedEdgeAsync(new RoadEdgeRecordDto
        {
            World = World, Geometry = new[] { P(300, 64, 100), P(300, 64, 200) }, AvgWidth = 2
        });

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateEdgeAsync(recorded.Id, new RoadEdgeUpdateDto { Confirmed = true }));
    }

    // ------------------------------------------------------------- Proposals (D5)

    [Fact]
    public async Task Proposal_RoundTripsThroughGetListAndDelete()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Build());

        var saved = await _service.SaveProposalAsync(World, 0, 0, Proposal(rejected: "[{\"n\":4}]"));
        Assert.Equal((1, 1, "Pandi"), (saved.AddedCount, saved.RejectedCount, saved.CreatedBy));

        var read = await _service.GetProposalAsync(World, 0, 0);
        Assert.NotNull(read);
        Assert.Equal("EDGE_ADDED", read!.Items[0].GetProperty("kind").GetString());
        Assert.Equal(4, read.Rejected[0].GetProperty("n").GetInt32());
        Assert.Equal(read.TileVersion, (await GraphAsync()).Tile.Version);

        var listed = Assert.Single(await _service.ListProposalsAsync(World));
        Assert.Equal((0, 0, 1), (listed.TileX, listed.TileZ, listed.AddedCount));
        Assert.Empty(await _service.ListProposalsAsync("other"));

        Assert.True(await _service.DeleteProposalAsync(World, 0, 0));
        Assert.Null(await _service.GetProposalAsync(World, 0, 0));
        Assert.False(await _service.DeleteProposalAsync(World, 0, 0));
    }

    [Fact]
    public async Task Proposal_KeepsTheBuildsCountsAndWarnings()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Build());
        var dto = Proposal();
        dto.CellCount = 900;
        dto.LevelCount = 2;
        dto.Warnings = new List<string> { "Prune matched nothing (stale; unprune it) (node 12)" };

        await _service.SaveProposalAsync(World, 0, 0, dto);
        var read = (await _service.GetProposalAsync(World, 0, 0))!;

        Assert.Equal((900, 2), (read.CellCount, read.LevelCount));
        Assert.Equal(dto.Warnings, read.Warnings);
        dto.CellCount = -1;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveProposalAsync(World, 0, 0, dto));
    }

    [Fact]
    public async Task Proposal_IsReplacedBySaving()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Build());
        await _service.SaveProposalAsync(World, 0, 0, Proposal());

        var replaced = await _service.SaveProposalAsync(World, 0, 0, Proposal(items: "[]", rejected: "[{\"n\":1},{\"n\":2}]"));

        Assert.Equal(0, replaced.Items.GetArrayLength());
        Assert.Equal(2, replaced.RejectedCount);
        Assert.Single(_db.RoadTileProposals);
    }

    [Fact]
    public async Task Upsert_ClearsThePendingItems_ButKeepsTheRejectedList()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Build());
        await _service.SaveProposalAsync(World, 0, 0, Proposal(rejected: "[{\"n\":3}]"));

        await _service.UpsertTileGraphAsync(World, 0, 0, Build());

        var after = await _service.GetProposalAsync(World, 0, 0);
        Assert.NotNull(after);
        Assert.Equal(0, after!.Items.GetArrayLength());
        Assert.Equal(0, after.AddedCount);
        Assert.Equal(1, after.Rejected.GetArrayLength());
    }

    [Fact]
    public async Task Proposal_IsRefusedForUnknownOrUnbuiltTiles_AndForNonArrays()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SaveProposalAsync(World, 0, 0, Proposal()));
        await _service.MarkTileDirtyAsync(World, 0, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveProposalAsync(World, 0, 0, Proposal()));

        await _service.UpsertTileGraphAsync(World, 0, 0, Build());
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveProposalAsync(World, 0, 0, Proposal(items: "{}")));
        var negative = Proposal();
        negative.RemovedCount = -1;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveProposalAsync(World, 0, 0, negative));
        var longName = Proposal();
        longName.CreatedBy = new string('x', 65);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveProposalAsync(World, 0, 0, longName));
    }

    [Fact]
    public async Task Proposal_WithoutItemsOrRejected_StoresEmptyArrays()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Build());

        var saved = await _service.SaveProposalAsync(World, 0, 0, new RoadTileProposalUpsertDto { BaseVersion = 1, BuilderVersion = 5 });

        Assert.Equal(JsonValueKind.Array, saved.Items.ValueKind);
        Assert.Equal(0, saved.Rejected.GetArrayLength());
    }
}
