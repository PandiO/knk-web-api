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
/// Road navigation rev. 5 (docs/specs/navigation/DESIGN.md §3.5, §5.6 step 4): moving a node and
/// designating a Junction or Anchor as the centre of a designed plaza, against the real repository
/// on EF InMemory.
/// </summary>
public class RoadNodeEditTests : IDisposable
{
    private const string World = "world";
    private readonly KnKDbContext _db;
    private readonly RoadNetworkService _service;

    public RoadNodeEditTests()
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

    /// <summary>Boundary at x=0, junction at x=200, endpoint at x=300, all at z=100 in tile (0,0).</summary>
    private static RoadTileGraphUpsertDto Road(int? b = null, int? j = null, int? e = null) => new()
    {
        BuilderVersion = 1, CellCount = 100, LevelCount = 1,
        Nodes =
        {
            Node("b", 0, 100, RoadNodeKind.Boundary, b),
            Node("j", 200, 100, RoadNodeKind.Junction, j),
            Node("e", 300, 100, RoadNodeKind.Endpoint, e)
        },
        Edges =
        {
            Edge("b", "j", new[] { P(0, 64, 100), P(100, 64, 100), P(200, 64, 100) }),
            Edge("j", "e", new[] { P(200, 64, 100), P(300, 64, 100) })
        }
    };

    private async Task<RoadTileGraphDto> GraphAsync() => (await _service.GetTileGraphAsync(World, 0, 0))!;

    private async Task<RoadNodeDto> NodeOfKindAsync(RoadNodeKind kind) => (await GraphAsync()).Nodes.Single(n => n.Kind == kind);

    [Fact]
    public async Task Move_KeepsTheTile_LocksTheNode_AndTheEdgeEndsFollow()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Road());
        var junction = await NodeOfKindAsync(RoadNodeKind.Junction);
        var version = (await GraphAsync()).Tile.Version;

        var moved = await _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { X = 205, Y = 66, Z = 104 });

        Assert.Equal((205, 66, 104, true), (moved.X, moved.Y, moved.Z, moved.Locked));
        var graph = await GraphAsync();
        Assert.Equal(version + 1, graph.Tile.Version);
        foreach (var edge in graph.Edges)
        {
            var end = edge.FromNodeId == junction.Id ? edge.Geometry.First() : edge.Geometry.Last();
            Assert.Equal(P(205, 66, 104), end);
            Assert.True(edge.MaxZ >= 104 && edge.MaxY >= 66, "bounding box follows the moved end");
        }
        var westEdge = graph.Edges.Single(e => e.Geometry.Length == 3);
        Assert.Contains(westEdge.Geometry, p => p.SequenceEqual(P(0, 64, 100))); // the far end stays put
    }

    [Fact]
    public async Task Move_IsRefusedAcrossTheTileBorder_OntoAnotherNode_OrWithoutAllThreeCoordinates()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Road());
        var junction = await NodeOfKindAsync(RoadNodeKind.Junction);
        var endpoint = await NodeOfKindAsync(RoadNodeKind.Endpoint);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { X = 600, Y = 64, Z = 100 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { X = endpoint.X, Y = endpoint.Y, Z = endpoint.Z }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { X = 210 }));
        Assert.Equal(200, (await NodeOfKindAsync(RoadNodeKind.Junction)).X);
    }

    [Fact]
    public async Task Plaza_IsSetAndCleared_OnJunctionsAndAnchors_AndLocksTheNode()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Road());
        var junction = await NodeOfKindAsync(RoadNodeKind.Junction);
        var anchor = await _service.CreateAnchorAsync(new RoadNodeAnchorDto { World = World, X = 100, Y = 64, Z = 100 });

        var plaza = await _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { PlazaRadius = 12 });
        Assert.Equal((12, true), (plaza.PlazaRadius, plaza.Locked));
        Assert.Equal(6, (await _service.UpdateNodeAsync(anchor.Id, new RoadNodeUpdateDto { PlazaRadius = 6 })).PlazaRadius);

        var cleared = await _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { ClearPlaza = true });
        Assert.Null(cleared.PlazaRadius);
    }

    [Fact]
    public async Task Plaza_IsRefusedOnOtherKinds_OutOfRange_AndWhenTheKindChangesAway()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Road());
        var junction = await NodeOfKindAsync(RoadNodeKind.Junction);
        var endpoint = await NodeOfKindAsync(RoadNodeKind.Endpoint);
        var boundary = await NodeOfKindAsync(RoadNodeKind.Boundary);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateNodeAsync(endpoint.Id, new RoadNodeUpdateDto { PlazaRadius = 8 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateNodeAsync(boundary.Id, new RoadNodeUpdateDto { PlazaRadius = 8 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { PlazaRadius = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { PlazaRadius = RoadNetworkService.MaxPlazaRadius + 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { PlazaRadius = 8, ClearPlaza = true }));

        await _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { PlazaRadius = 8 });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { Kind = RoadNodeKind.Endpoint }));
    }

    [Fact]
    public async Task APlazaCentre_KeepsItsPositionAndRadiusThroughARebuild()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, Road());
        var graph = await GraphAsync();
        int Id(RoadNodeKind kind) => graph.Nodes.Single(n => n.Kind == kind).Id;
        await _service.UpdateNodeAsync(Id(RoadNodeKind.Junction), new RoadNodeUpdateDto { PlazaRadius = 10, X = 202, Y = 64, Z = 100 });

        // The builder sends its junction 2 blocks off; the locked plaza centre stays where the admin put it.
        await _service.UpsertTileGraphAsync(World, 0, 0, Road(Id(RoadNodeKind.Boundary), Id(RoadNodeKind.Junction), Id(RoadNodeKind.Endpoint)));

        var junction = await NodeOfKindAsync(RoadNodeKind.Junction);
        Assert.Equal((202, 10, true), (junction.X, junction.PlazaRadius, junction.Locked));
    }
}
