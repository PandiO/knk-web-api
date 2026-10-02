using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Road navigation Phase 1 (docs/specs/navigation/IMPLEMENTATION_PLAN.md §1.7): the tile graph
/// upsert (matching, deletions, stitching, labelling, components, versions), the review actions
/// and the validation rules, against the real repository on EF InMemory.
/// </summary>
public class RoadNetworkServiceTests : IDisposable
{
    private const string World = "world";
    private readonly KnKDbContext _db;
    private readonly RoadNetworkService _service;

    public RoadNetworkServiceTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _db.RoadProfiles.Add(new RoadProfile { Id = 1, Name = "Default road", RoadClass = RoadClass.Road });
        _db.RoadProfiles.Add(new RoadProfile { Id = 2, Name = "Trail", RoadClass = RoadClass.Path });
        _db.Streets.Add(new Street { Id = 1, Name = "High Street" });
        _db.Streets.Add(new Street { Id = 2, Name = "Mill Lane" });
        _db.Towns.Add(new Town { Id = 100, Name = "Rivia", Description = "", WgRegionId = "town_rivia" });
        _db.SaveChanges();

        _service = new RoadNetworkService(new RoadNetworkRepository(_db), Mapper());
    }

    public void Dispose() => _db.Dispose();

    public static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<RoadMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
    }).CreateMapper();

    private static int[] P(int x, int y, int z) => new[] { x, y, z };

    private static RoadTileGraphNodeDto Node(string key, int x, int z, RoadNodeKind kind = RoadNodeKind.Junction, int? existingId = null, int y = 64) =>
        new() { Key = key, X = x, Y = y, Z = z, Kind = kind, ExistingId = existingId };

    private static RoadTileGraphEdgeDto Edge(string from, string to, int[][] geometry, int? existingId = null, int? profileId = 1, double? length = null) =>
        new()
        {
            FromKey = from, ToKey = to, Geometry = geometry, ExistingId = existingId, ProfileId = profileId,
            Length = length ?? knkwebapi_v2.Services.Roads.RoadGeometry.PolylineLength(geometry), AvgWidth = 3
        };

    /// <summary>A straight west-east road across tile (0,0) at z = 100 with a junction in the middle:
    /// boundary B0 at x=0, junction J at x=200, boundary B1 at x=511.</summary>
    private static RoadTileGraphUpsertDto StraightRoad(int? b0 = null, int? j = null, int? b1 = null) => new()
    {
        BuilderVersion = 1, CellCount = 1500, LevelCount = 1,
        Nodes =
        {
            Node("b0", 0, 100, RoadNodeKind.Boundary, b0),
            Node("j", 200, 100, RoadNodeKind.Junction, j),
            Node("b1", 511, 100, RoadNodeKind.Boundary, b1)
        },
        Edges =
        {
            Edge("b0", "j", new[] { P(0, 64, 100), P(200, 64, 100) }),
            Edge("j", "b1", new[] { P(200, 64, 100), P(511, 64, 100) })
        }
    };

    private void AddStructure(int id, int streetId, double x, double y, double z)
    {
        _db.Locations.Add(new Location { Id = id, World = World, X = x, Y = y, Z = z });
        _db.Districts.Add(new District { Id = 1000 + id, Name = $"D{id}", Description = "", WgRegionId = $"d{id}", TownId = 100 });
        _db.Structures.Add(new Structure { Id = id, Name = $"House {id}", Description = "", WgRegionId = $"s{id}", StreetId = streetId, DistrictId = 1000 + id, LocationId = id });
        _db.SaveChanges();
    }

    // ------------------------------------------------------------ Upsert

    [Fact]
    public async Task FirstUpsert_CreatesNodesEdgesAndVersionOne()
    {
        var result = await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());

        Assert.Equal((3, 2, 0, 0), (result.NodesCreated, result.EdgesCreated, result.NodesDeleted, result.EdgesDeleted));
        Assert.Equal(1, result.Tile.Version);
        Assert.False(result.Tile.Dirty);
        Assert.Equal((3, 2, 1500), (result.Tile.NodeCount, result.Tile.EdgeCount, result.Tile.CellCount));

        var graph = await _service.GetTileGraphAsync(World, 0, 0);
        Assert.NotNull(graph);
        Assert.Equal(3, graph!.Nodes.Count);
        var edge = graph.Edges.Single(e => e.Geometry[0][0] == 0);
        Assert.True(edge.FromNodeId < edge.ToNodeId);
        Assert.Equal((0, 200, 64, 64), (edge.MinX, edge.MaxX, edge.MinY, edge.MaxY));
        Assert.Equal(200, edge.Length, 3);
        Assert.Equal(RoadEdgeSource.Detected, edge.Source);
        Assert.Equal(1, edge.ProfileId);
        // One component for the whole road.
        Assert.Single(graph.Nodes.Select(n => n.ComponentId).Distinct());
        Assert.Equal(graph.Nodes.Min(n => n.Id), graph.Nodes[0].ComponentId);
    }

    [Fact]
    public async Task IdenticalReUpsert_KeepsIdsAndBumpsVersion()
    {
        var first = await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var before = await _service.GetTileGraphAsync(World, 0, 0);
        var ids = before!.Nodes.ToDictionary(n => n.X, n => n.Id);

        var second = await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad(ids[0], ids[200], ids[511]));
        var after = await _service.GetTileGraphAsync(World, 0, 0);

        Assert.Equal((0, 3, 0, 2), (second.NodesCreated, second.NodesUpdated, second.EdgesCreated, second.EdgesUpdated));
        Assert.Equal(first.Tile.Version + 1, second.Tile.Version);
        Assert.Equal(before.Nodes.Select(n => n.Id).OrderBy(i => i), after!.Nodes.Select(n => n.Id).OrderBy(i => i));
        Assert.Equal(before.Edges.Select(e => e.Id).OrderBy(i => i), after.Edges.Select(e => e.Id).OrderBy(i => i));
    }

    [Fact]
    public async Task ReUpsertWithoutExistingIds_MatchesByPositionAndNodePair()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var before = await _service.GetTileGraphAsync(World, 0, 0);

        var second = await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var after = await _service.GetTileGraphAsync(World, 0, 0);

        Assert.Equal((0, 0), (second.NodesCreated, second.EdgesCreated));
        Assert.Equal(before!.Edges.Select(e => e.Id).OrderBy(i => i), after!.Edges.Select(e => e.Id).OrderBy(i => i));
    }

    [Fact]
    public async Task MissingNode_IsDeletedWithItsEdgesAndListed()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var before = await _service.GetTileGraphAsync(World, 0, 0);
        var b0 = before!.Nodes.Single(n => n.X == 0).Id;
        var j = before.Nodes.Single(n => n.X == 200).Id;

        // The road now ends at the junction: b1 disappeared.
        var payload = new RoadTileGraphUpsertDto
        {
            Nodes = { Node("b0", 0, 100, RoadNodeKind.Boundary, b0), Node("j", 200, 100, RoadNodeKind.Endpoint, j) },
            Edges = { Edge("b0", "j", new[] { P(0, 64, 100), P(200, 64, 100) }) }
        };
        var result = await _service.UpsertTileGraphAsync(World, 0, 0, payload);
        var after = await _service.GetTileGraphAsync(World, 0, 0);

        Assert.Equal((1, 1), (result.NodesDeleted, result.EdgesDeleted));
        Assert.Equal(511, Assert.Single(result.DeletedNodes).X);
        Assert.Equal(2, after!.Nodes.Count);
        Assert.Single(after.Edges);
        Assert.Equal(RoadNodeKind.Endpoint, after.Nodes.Single(n => n.X == 200).Kind);
        Assert.Empty(_db.RoadNodes.Where(n => n.X == 511));
    }

    [Fact]
    public async Task ManualLockedNodesAndRecordedEdgesSurviveARebuild()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var anchor = await _service.CreateAnchorAsync(new RoadNodeAnchorDto { World = World, X = 300, Y = 64, Z = 300, Name = "Well" });
        var recorded = await _service.CreateRecordedEdgeAsync(new RoadEdgeRecordDto
        {
            World = World, Geometry = new[] { P(300, 64, 300), P(300, 64, 380), P(300, 64, 400) }
        });
        var graph = await _service.GetTileGraphAsync(World, 0, 0);
        var j = graph!.Nodes.Single(n => n.X == 200).Id;
        var lockedResult = await _service.UpdateNodeAsync(j, new RoadNodeUpdateDto { Name = "Market", Locked = true });
        Assert.True(lockedResult.Locked);

        // The rebuild moved the junction by 2 blocks and knows nothing of the anchor or recording.
        var rebuilt = StraightRoad(graph.Nodes.Single(n => n.X == 0).Id, j, graph.Nodes.Single(n => n.X == 511).Id);
        rebuilt.Nodes.Single(n => n.Key == "j").X = 202;
        rebuilt.Edges[0].Geometry = new[] { P(0, 64, 100), P(202, 64, 100) };
        rebuilt.Edges[0].Length = 202;
        rebuilt.Edges[1].Geometry = new[] { P(202, 64, 100), P(511, 64, 100) };
        rebuilt.Edges[1].Length = 309;
        var result = await _service.UpsertTileGraphAsync(World, 0, 0, rebuilt);
        var after = await _service.GetTileGraphAsync(World, 0, 0);

        Assert.Equal(0, result.NodesDeleted);
        var junction = after!.Nodes.Single(n => n.Id == j);
        Assert.Equal((200, "Market", true), (junction.X, junction.Name, junction.Locked));
        Assert.Contains(after.Nodes, n => n.Id == anchor.Id && n.Source == RoadNodeSource.Manual && n.Name == "Well");
        var kept = Assert.Single(after.Edges, e => e.Source == RoadEdgeSource.Recorded);
        Assert.Equal(recorded.Id, kept.Id);
        // The recording's far end became an Anchor and is still there.
        Assert.Contains(after.Nodes, n => n.X == 300 && n.Z == 400 && n.Kind == RoadNodeKind.Anchor);
    }

    [Fact]
    public async Task AdminStreetLabelFlagsAndCostSurviveARebuild()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var graph = await _service.GetTileGraphAsync(World, 0, 0);
        var edge = graph!.Edges.Single(e => e.Geometry[0][0] == 0);
        await _service.UpdateEdgeAsync(edge.Id, new RoadEdgeUpdateDto { StreetId = 2, CostMultiplier = 2.5, Flags = new() { "NoGps" } });
        var ids = graph.Nodes.ToDictionary(n => n.X, n => n.Id);

        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad(ids[0], ids[200], ids[511]));
        var after = (await _service.GetTileGraphAsync(World, 0, 0))!.Edges.Single(e => e.Id == edge.Id);

        Assert.Equal((2, RoadStreetSource.Manual, 2.5), (after.StreetId, after.StreetSource, after.CostMultiplier));
        Assert.Equal(new[] { "NoGps" }, after.Flags);
    }

    [Fact]
    public async Task InferredStreetLabelsComeFromTheStructuresAlongTheRoad()
    {
        AddStructure(1, 1, 50, 64, 103);
        AddStructure(2, 1, 120, 64, 97);
        AddStructure(3, 2, 400, 64, 103); // one house on Mill Lane past the junction: not enough for a label there

        var result = await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var graph = await _service.GetTileGraphAsync(World, 0, 0);

        var west = graph!.Edges.Single(e => e.Geometry[0][0] == 0);
        var east = graph.Edges.Single(e => e.Geometry[0][0] == 200);
        Assert.Equal((1, RoadStreetSource.Inferred), (west.StreetId, west.StreetSource));
        Assert.Equal((2, RoadStreetSource.Inferred), (east.StreetId, east.StreetSource));
        Assert.Equal((2, 0), (result.LabelledEdges, result.UnlabelledEdges));

        var meta = await _service.GetMetaAsync(World);
        Assert.Equal(new[] { "High Street", "Mill Lane" }, meta.Streets.Select(s => s.Name));
        Assert.Single(meta.Components);
    }

    [Fact]
    public async Task LabelConflictsBecomeTileWarnings()
    {
        AddStructure(1, 1, 50, 64, 103);
        AddStructure(2, 2, 150, 64, 103);

        var result = await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());

        Assert.Single(result.Conflicts);
        Assert.Contains(result.Tile.Warnings, w => w.Contains("majority"));
    }

    [Fact]
    public async Task StitchEdges_JoinAdjacentTilesWhicheverIsBuiltFirst()
    {
        // Tile (0,0)'s boundary node at x=511 and tile (1,0)'s at x=512, one block apart.
        var east = new RoadTileGraphUpsertDto
        {
            Nodes = { Node("b", 512, 100, RoadNodeKind.Boundary), Node("e", 700, 100, RoadNodeKind.Endpoint) },
            Edges = { Edge("b", "e", new[] { P(512, 64, 100), P(700, 64, 100) }) }
        };

        var eastResult = await _service.UpsertTileGraphAsync(World, 1, 0, east);
        Assert.Equal(0, eastResult.StitchEdges);
        var westResult = await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        Assert.Equal(1, westResult.StitchEdges);

        var west = await _service.GetTileGraphAsync(World, 0, 0);
        var stitch = Assert.Single(west!.Edges, e => e.Source == RoadEdgeSource.Stitch);
        Assert.Equal(1.0, stitch.Length, 6);
        var eastNodes = (await _service.GetTileGraphAsync(World, 1, 0))!.Nodes;
        Assert.Contains(stitch.FromNodeId, eastNodes.Select(n => n.Id).Concat(west.Nodes.Select(n => n.Id)));
        Assert.Contains(stitch.ToNodeId, eastNodes.Select(n => n.Id).Concat(west.Nodes.Select(n => n.Id)));

        // Components merged across the stitch.
        var meta = await _service.GetMetaAsync(World);
        var component = Assert.Single(meta.Components);
        Assert.Equal(5, component.NodeCount);
    }

    [Fact]
    public async Task RebuildingATile_ReplacesTheStitchAndBumpsTheNeighbourThatOwnedIt()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var east = new RoadTileGraphUpsertDto
        {
            Nodes = { Node("b", 512, 100, RoadNodeKind.Boundary), Node("e", 700, 100, RoadNodeKind.Endpoint) },
            Edges = { Edge("b", "e", new[] { P(512, 64, 100), P(700, 64, 100) }) }
        };
        var eastResult = await _service.UpsertTileGraphAsync(World, 1, 0, east);
        Assert.Equal(1, eastResult.StitchEdges); // east owns the stitch now
        var westVersion = (await _service.GetTileGraphAsync(World, 0, 0))!.Tile.Version;
        var eastVersion = eastResult.Tile.Version;

        // Rebuild west: it takes the stitch over and east loses an owned edge → east's version bumps.
        var westIds = (await _service.GetTileGraphAsync(World, 0, 0))!.Nodes.ToDictionary(n => n.X, n => n.Id);
        var westResult = await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad(westIds[0], westIds[200], westIds[511]));

        Assert.Equal(1, westResult.StitchEdges);
        Assert.Equal(westVersion + 1, westResult.Tile.Version);
        var eastAfter = await _service.GetTileGraphAsync(World, 1, 0);
        Assert.Equal(eastVersion + 1, eastAfter!.Tile.Version);
        Assert.Contains(eastAfter.Tile.Id, westResult.BumpedTileIds);
        Assert.DoesNotContain(eastAfter.Edges, e => e.Source == RoadEdgeSource.Stitch);
        Assert.Single(_db.RoadEdges.Where(e => e.Source == RoadEdgeSource.Stitch));

        // Every edge of each tile references nodes that exist.
        var nodeIds = _db.RoadNodes.Select(n => n.Id).ToHashSet();
        Assert.All(_db.RoadEdges, e => Assert.True(nodeIds.Contains(e.FromNodeId) && nodeIds.Contains(e.ToNodeId)));
    }

    [Fact]
    public async Task RemovingABoundaryNode_DropsTheNeighboursStitchAndBumpsIt()
    {
        var east = new RoadTileGraphUpsertDto
        {
            Nodes = { Node("b", 512, 100, RoadNodeKind.Boundary), Node("e", 700, 100, RoadNodeKind.Endpoint) },
            Edges = { Edge("b", "e", new[] { P(512, 64, 100), P(700, 64, 100) }) }
        };
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var eastResult = await _service.UpsertTileGraphAsync(World, 1, 0, east);
        var westIds = (await _service.GetTileGraphAsync(World, 0, 0))!.Nodes.ToDictionary(n => n.X, n => n.Id);

        // West's road now ends before the border: its boundary node b1 is gone.
        var shorter = new RoadTileGraphUpsertDto
        {
            Nodes = { Node("b0", 0, 100, RoadNodeKind.Boundary, westIds[0]), Node("j", 200, 100, RoadNodeKind.Endpoint, westIds[200]) },
            Edges = { Edge("b0", "j", new[] { P(0, 64, 100), P(200, 64, 100) }) }
        };
        var westResult = await _service.UpsertTileGraphAsync(World, 0, 0, shorter);

        Assert.Equal(0, westResult.StitchEdges);
        Assert.Empty(_db.RoadEdges.Where(e => e.Source == RoadEdgeSource.Stitch));
        Assert.Equal(eastResult.Tile.Version + 1, (await _service.GetTileGraphAsync(World, 1, 0))!.Tile.Version);
        Assert.Equal(2, (await _service.GetMetaAsync(World)).Components.Count);
    }

    [Fact]
    public async Task Dirty_MarksEdgesStaleAndTheUpsertClearsIt()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var before = await _service.GetTileGraphAsync(World, 0, 0);

        var dirty = await _service.MarkTileDirtyAsync(World, 0, 0);
        var stale = await _service.GetTileGraphAsync(World, 0, 0);
        Assert.True(dirty.Dirty);
        Assert.Equal(before!.Tile.Version + 1, dirty.Version);
        Assert.All(stale!.Edges, e => Assert.Equal(RoadEdgeStatus.Stale, e.Status));

        var ids = before.Nodes.ToDictionary(n => n.X, n => n.Id);
        var rebuilt = await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad(ids[0], ids[200], ids[511]));
        Assert.False(rebuilt.Tile.Dirty);
        Assert.All((await _service.GetTileGraphAsync(World, 0, 0))!.Edges, e => Assert.Equal(RoadEdgeStatus.Ok, e.Status));
    }

    [Fact]
    public async Task MarkingAnUnknownTileDirty_CreatesIt()
    {
        var tile = await _service.MarkTileDirtyAsync(World, 3, -2);

        Assert.True(tile.Dirty);
        Assert.Null(tile.BuiltAt);
        Assert.Single(await _service.ListTilesAsync(World));
    }

    // ---------------------------------------------------------- Validation

    [Fact]
    public async Task Validation_NodeOutsideTheTile()
    {
        var payload = StraightRoad();
        payload.Nodes[1].X = 600;

        var error = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertTileGraphAsync(World, 0, 0, payload));
        Assert.Contains("outside the tile", error.Message);
    }

    [Fact]
    public async Task Validation_BoundaryNodeOffTheBorder()
    {
        var payload = StraightRoad();
        payload.Nodes[0].X = 5;
        payload.Edges[0].Geometry = new[] { P(5, 64, 100), P(200, 64, 100) };
        payload.Edges[0].Length = 195;

        var error = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertTileGraphAsync(World, 0, 0, payload));
        Assert.Contains("not on the tile border", error.Message);
    }

    [Fact]
    public async Task Validation_GeometryFarFromItsNode()
    {
        var payload = StraightRoad();
        payload.Edges[0].Geometry = new[] { P(0, 64, 100), P(197, 64, 100) };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertTileGraphAsync(World, 0, 0, payload));
        Assert.Contains("within 1.5 blocks", error.Message);
    }

    [Fact]
    public async Task Validation_LengthShorterThanTheStraightLine()
    {
        var payload = StraightRoad();
        payload.Edges[0].Length = 150;

        var error = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertTileGraphAsync(World, 0, 0, payload));
        Assert.Contains("straight-line", error.Message);
    }

    [Fact]
    public async Task Validation_BadKindDuplicateKeysSelfLoopsAndUnknownIds()
    {
        var badKind = StraightRoad();
        badKind.Nodes[1].Kind = (RoadNodeKind)42;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertTileGraphAsync(World, 0, 0, badKind));

        var duplicateKey = StraightRoad();
        duplicateKey.Nodes[2].Key = "j";
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertTileGraphAsync(World, 0, 0, duplicateKey));

        var loop = StraightRoad();
        loop.Edges[0].ToKey = "b0";
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertTileGraphAsync(World, 0, 0, loop));

        var foreignNode = StraightRoad(b0: 9999);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertTileGraphAsync(World, 0, 0, foreignNode));

        var unknownProfile = StraightRoad();
        unknownProfile.Edges[0].ProfileId = 77;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertTileGraphAsync(World, 0, 0, unknownProfile));

        Assert.Empty(_db.RoadNodes); // nothing was written by the failed calls
    }

    [Fact]
    public async Task Validation_EdgeToAnExistingNodeOfAnotherTileByIdKey()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var b1 = (await _service.GetTileGraphAsync(World, 0, 0))!.Nodes.Single(n => n.X == 511);

        var east = new RoadTileGraphUpsertDto
        {
            Nodes = { Node("e", 700, 100, RoadNodeKind.Endpoint) },
            Edges = { Edge($"id:{b1.Id}", "e", new[] { P(511, 64, 100), P(700, 64, 100) }) }
        };
        var result = await _service.UpsertTileGraphAsync(World, 1, 0, east);

        Assert.Equal(1, result.EdgesCreated);
        var edge = Assert.Single((await _service.GetTileGraphAsync(World, 1, 0))!.Edges);
        Assert.Equal(b1.Id, Math.Min(edge.FromNodeId, edge.ToNodeId));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertTileGraphAsync(World, 1, 0, new RoadTileGraphUpsertDto
        {
            Nodes = { Node("e", 700, 100, RoadNodeKind.Endpoint) },
            Edges = { Edge("id:424242", "e", new[] { P(511, 64, 100), P(700, 64, 100) }) }
        }));
    }

    // ------------------------------------------------------- Review actions

    [Fact]
    public async Task MergeNodes_RepointsEdgesDropsLoopsAndDuplicatesAndRecomputesComponents()
    {
        // Two junctions 2 blocks apart, each on its own road, plus an edge between them.
        var payload = new RoadTileGraphUpsertDto
        {
            Nodes =
            {
                Node("a", 100, 100, RoadNodeKind.Endpoint), Node("j1", 200, 100), Node("j2", 202, 100),
                Node("b", 300, 100, RoadNodeKind.Endpoint), Node("c", 200, 200, RoadNodeKind.Endpoint), Node("d", 202, 200, RoadNodeKind.Endpoint)
            },
            Edges =
            {
                Edge("a", "j1", new[] { P(100, 64, 100), P(200, 64, 100) }),
                Edge("j1", "j2", new[] { P(200, 64, 100), P(202, 64, 100) }),
                Edge("j2", "b", new[] { P(202, 64, 100), P(300, 64, 100) }),
                Edge("j1", "c", new[] { P(200, 64, 100), P(200, 64, 200) }),
                Edge("j2", "d", new[] { P(202, 64, 100), P(202, 64, 200) })
            }
        };
        await _service.UpsertTileGraphAsync(World, 0, 0, payload);
        var graph = await _service.GetTileGraphAsync(World, 0, 0);
        var j1 = graph!.Nodes.Single(n => n.X == 200 && n.Z == 100);
        var j2 = graph.Nodes.Single(n => n.X == 202 && n.Z == 100);
        var versionBefore = graph.Tile.Version;

        var kept = await _service.MergeNodesAsync(new RoadNodeMergeDto { KeepNodeId = j1.Id, MergeNodeId = j2.Id });
        var after = await _service.GetTileGraphAsync(World, 0, 0);

        Assert.True(kept.Locked);
        Assert.DoesNotContain(after!.Nodes, n => n.Id == j2.Id);
        Assert.Equal(4, after.Edges.Count); // the j1-j2 loop is gone
        Assert.All(after.Edges, e => Assert.True(e.FromNodeId == j1.Id || e.ToNodeId == j1.Id));
        Assert.All(after.Edges, e => Assert.True(e.FromNodeId < e.ToNodeId));
        var toB = after.Edges.Single(e => e.MaxX == 300);
        Assert.Equal(new[] { 200, 64, 100 }, toB.Geometry[0]);
        Assert.Equal(versionBefore + 1, after.Tile.Version);
        Assert.Single(after.Nodes.Select(n => n.ComponentId).Distinct());
    }

    [Fact]
    public async Task RecordedEdge_SnapsToNearbyNodesAndCreatesAnchorsElsewhere()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var junction = (await _service.GetTileGraphAsync(World, 0, 0))!.Nodes.Single(n => n.X == 200);

        var edge = await _service.CreateRecordedEdgeAsync(new RoadEdgeRecordDto
        {
            World = World, Geometry = new[] { P(201, 65, 102), P(201, 65, 150), P(201, 70, 160) }, StreetId = 2
        });
        var graph = await _service.GetTileGraphAsync(World, 0, 0);

        Assert.Equal(RoadEdgeSource.Recorded, edge.Source);
        Assert.Equal(junction.Id, edge.FromNodeId);
        Assert.False(junction.Locked);
        Assert.True(graph!.Nodes.Single(n => n.Id == junction.Id).Locked, "the detected node it snapped to is locked now");
        var anchor = graph!.Nodes.Single(n => n.Id == edge.ToNodeId);
        Assert.Equal((201, 70, 160, RoadNodeKind.Anchor, RoadNodeSource.Manual, true), (anchor.X, anchor.Y, anchor.Z, anchor.Kind, anchor.Source, anchor.Locked));
        Assert.Equal((2, RoadStreetSource.Manual), (edge.StreetId, edge.StreetSource));
        Assert.Equal(knkwebapi_v2.Services.Roads.RoadGeometry.PolylineLength(new[] { P(201, 65, 102), P(201, 65, 150), P(201, 70, 160) }), edge.Length, 6);
        Assert.Single(graph.Nodes.Select(n => n.ComponentId).Distinct());

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateRecordedEdgeAsync(new RoadEdgeRecordDto
        {
            World = World, Geometry = new[] { P(200, 64, 100), P(201, 70, 160) }
        }));
    }

    [Fact]
    public async Task UpdateEdge_PropagatesAManualStreetAlongTheRoad()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var graph = await _service.GetTileGraphAsync(World, 0, 0);
        var west = graph!.Edges.Single(e => e.Geometry[0][0] == 0);
        var east = graph.Edges.Single(e => e.Geometry[0][0] == 200);

        var result = await _service.UpdateEdgeAsync(west.Id, new RoadEdgeUpdateDto { StreetId = 1, Propagate = true });

        Assert.Equal(new[] { west.Id, east.Id }, result.ChangedEdgeIds);
        var after = await _service.GetTileGraphAsync(World, 0, 0);
        Assert.All(after!.Edges, e => Assert.Equal((1, RoadStreetSource.Manual), (e.StreetId, e.StreetSource)));
        Assert.Equal(graph.Tile.Version + 1, after.Tile.Version);

        var cleared = await _service.UpdateEdgeAsync(west.Id, new RoadEdgeUpdateDto { ClearStreet = true, Flags = new() { "Closed", "oneway" } });
        Assert.Equal((null, RoadStreetSource.None), (cleared.Edge.StreetId, cleared.Edge.StreetSource));
        Assert.Equal(new[] { "Oneway", "Closed" }, cleared.Edge.Flags);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateEdgeAsync(west.Id, new RoadEdgeUpdateDto { Flags = new() { "Fast" } }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateEdgeAsync(9999, new RoadEdgeUpdateDto()));
    }

    [Fact]
    public async Task DeleteEdge_SplitsTheComponent()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var graph = await _service.GetTileGraphAsync(World, 0, 0);

        Assert.True(await _service.DeleteEdgeAsync(graph!.Edges[0].Id));
        Assert.False(await _service.DeleteEdgeAsync(graph.Edges[0].Id));

        var after = await _service.GetTileGraphAsync(World, 0, 0);
        Assert.Single(after!.Edges);
        Assert.Equal(2, after.Nodes.Select(n => n.ComponentId).Distinct().Count());
        Assert.Equal(graph.Tile.Version + 1, after.Tile.Version);
    }

    [Fact]
    public async Task EdgeSearch_FiltersByWorldStreetUnlabelledAndStale()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var graph = await _service.GetTileGraphAsync(World, 0, 0);
        await _service.UpdateEdgeAsync(graph!.Edges[0].Id, new RoadEdgeUpdateDto { StreetId = 1 });

        var labelled = await _service.SearchEdgesAsync(new PagedQueryDto { Filters = new() { ["world"] = World, ["streetId"] = "1" } });
        var unlabelled = await _service.SearchEdgesAsync(new PagedQueryDto { Filters = new() { ["unlabelled"] = "true" } });
        var stale = await _service.SearchEdgesAsync(new PagedQueryDto { Filters = new() { ["stale"] = "true" } });
        var other = await _service.SearchEdgesAsync(new PagedQueryDto { Filters = new() { ["world"] = "nether" } });

        Assert.Equal(graph.Edges[0].Id, Assert.Single(labelled.Items).Id);
        Assert.Equal(graph.Edges[1].Id, Assert.Single(unlabelled.Items).Id);
        Assert.Empty(stale.Items);
        Assert.Equal(0, other.TotalCount);
    }

    // ------------------------------------------------- Profiles, seeds, meta

    [Fact]
    public async Task Profiles_CreateUpdateDeleteWithValidation()
    {
        var created = await _service.CreateProfileAsync(new RoadProfileUpsertDto
        {
            Name = "Kardenna main street", RoadClass = RoadClass.Main, WidthMin = 3, WidthMax = 9,
            Materials = { new RoadMaterialDto { Material = "STONE_BRICKS", Role = RoadMaterialRole.Surface, Ambiguous = true } },
            ScopeTownIds = new() { 100 }
        });
        Assert.Equal(new[] { 100 }, created.ScopeTownIds);
        Assert.Equal("STONE_BRICKS", Assert.Single(created.Materials).Material);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, created.Stats!.Value.ValueKind);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateProfileAsync(new RoadProfileUpsertDto { Name = "Default road" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateProfileAsync(new RoadProfileUpsertDto { Name = "x", WidthMin = 5, WidthMax = 2 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateProfileAsync(new RoadProfileUpsertDto
        {
            Name = "x", Materials = { new RoadMaterialDto { Material = "stone bricks" } }
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateProfileAsync(new RoadProfileUpsertDto { Name = "x", ScopeTownIds = new() { 100, 555 } }));

        var updated = await _service.UpdateProfileAsync(created.Id, new RoadProfileUpsertDto
        {
            Name = "Kardenna main street", RoadClass = RoadClass.Main, WidthMin = 3, WidthMax = 9, SampleCount = 10,
            Stats = System.Text.Json.JsonDocument.Parse("{\"widths\":[3,4]}").RootElement
        });
        Assert.Equal(10, updated.SampleCount);
        Assert.Equal("[3,4]", updated.Stats!.Value.GetProperty("widths").GetRawText());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateProfileAsync(999, new RoadProfileUpsertDto { Name = "y" }));

        // Deleting a profile un-references its edges (SetNull, done explicitly for the in-memory provider).
        var payload = StraightRoad();
        payload.Edges.ForEach(e => e.ProfileId = created.Id);
        await _service.UpsertTileGraphAsync(World, 0, 0, payload);
        Assert.True(await _service.DeleteProfileAsync(created.Id));
        Assert.All((await _service.GetTileGraphAsync(World, 0, 0))!.Edges, e => Assert.Null(e.ProfileId));
        Assert.False(await _service.DeleteProfileAsync(created.Id));
    }

    [Fact]
    public async Task SurveysAndSeeds_RoundTrip()
    {
        var survey = await _service.CreateSurveyAsync(new RoadSurveyCreateDto
        {
            World = World, ProfileId = 1, StartedAt = new DateTime(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc), EndedAt = new DateTime(2026, 9, 27, 18, 5, 0, DateTimeKind.Utc),
            SampleCount = 120, Breadcrumb = { new RoadBreadcrumbPointDto { X = 1, Y = 64, Z = 1, OnRoad = true } },
            Stats = System.Text.Json.JsonDocument.Parse("{\"widths\":{\"3\":100}}").RootElement
        }, startedByUserId: 7);
        Assert.Equal(7, survey.StartedByUserId);
        Assert.True(Assert.Single(survey.Breadcrumb).OnRoad);
        Assert.Equal("100", survey.Stats!.Value.GetProperty("widths").GetProperty("3").GetRawText());
        Assert.Single(await _service.ListSurveysAsync(World));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateSurveyAsync(new RoadSurveyCreateDto { World = World, StartedAt = DateTime.UtcNow, ProfileId = 99 }, null));

        var seed = await _service.CreateSeedAsync(new RoadSeedCreateDto { World = World, X = 1, Y = 64, Z = 2, Source = RoadSeedSource.Survey, SurveyId = survey.Id, Note = "bridge" });
        Assert.Equal(RoadSeedSource.Survey, seed.Source);
        Assert.Single(await _service.ListSeedsAsync(World));
        Assert.True(await _service.DeleteSeedAsync(seed.Id));
        Assert.False(await _service.DeleteSeedAsync(seed.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateSeedAsync(new RoadSeedCreateDto { World = World, SurveyId = 999 }));
    }

    [Fact]
    public async Task SeedLocations_ListDomainsWhoseLocationIsInTheBox()
    {
        AddStructure(1, 1, 50, 64, 103);
        AddStructure(2, 1, 900, 64, 103);
        _db.Locations.Add(new Location { Id = 50, World = World, X = 10.7, Y = 70, Z = 20.2 });
        _db.Towns.Add(new Town { Id = 101, Name = "Novigrad", Description = "", WgRegionId = "t2", LocationId = 50 });
        _db.SaveChanges();

        var inBox = await _service.GetSeedLocationsAsync(World, 0, 0, 511, 511);

        Assert.Equal(new[] { ("House 1", "Structure", 50), ("Novigrad", "Town", 10) }, inBox.Select(l => (l.Name, l.DomainType, l.X)).OrderBy(l => l.Item1));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetSeedLocationsAsync(World, 10, 0, 0, 0));
    }

    [Fact]
    public async Task StreetRoad_ListsTheStreetsEdgesAndNodes()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var graph = await _service.GetTileGraphAsync(World, 0, 0);
        await _service.UpdateEdgeAsync(graph!.Edges[0].Id, new RoadEdgeUpdateDto { StreetId = 1 });

        var road = await _service.GetStreetRoadAsync(1);

        Assert.Equal(("High Street", 1), (road!.Name, road.EdgeCount));
        Assert.Equal(graph.Edges[0].Length, road.TotalLength, 6);
        Assert.Equal(2, road.Nodes.Count);
        Assert.Null(await _service.GetStreetRoadAsync(999));
    }

    [Fact]
    public async Task UpdateNode_RenamesRekindsAndLocks()
    {
        await _service.UpsertTileGraphAsync(World, 0, 0, StraightRoad());
        var graph = await _service.GetTileGraphAsync(World, 0, 0);
        var junction = graph!.Nodes.Single(n => n.X == 200);

        var renamed = await _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { Name = "  Market  " });
        Assert.Equal(("Market", true), (renamed.Name, renamed.Locked));

        var unlocked = await _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { Locked = false, Kind = RoadNodeKind.Anchor });
        Assert.Equal((RoadNodeKind.Anchor, false), (unlocked.Kind, unlocked.Locked));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateNodeAsync(junction.Id, new RoadNodeUpdateDto { Kind = RoadNodeKind.Boundary }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateNodeAsync(9999, new RoadNodeUpdateDto { Name = "x" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAnchorAsync(new RoadNodeAnchorDto { World = World, X = 200, Y = 64, Z = 100 }));
    }
}
