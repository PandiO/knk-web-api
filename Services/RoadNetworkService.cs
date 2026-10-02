using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Roads;

namespace knkwebapi_v2.Services;

/// <summary>
/// Road navigation (docs/specs/navigation/DESIGN.md §3, §5.6-5.11; IMPLEMENTATION_PLAN.md Phase
/// 1.4). The tile upsert is the heart of it: one transaction that matches the builder's nodes and
/// edges to the stored ones by id, keeps everything admins made, stitches the tile to its
/// neighbours (plan D7), labels streets from the Structures along the roads (plan D6) and
/// recomputes the world's components.
/// </summary>
public class RoadNetworkService : IRoadNetworkService
{
    /// <summary>A geometry end may sit this far from its node (DESIGN §3.8).</summary>
    public const double GeometryEndTolerance = 1.5;
    /// <summary>A recorded edge's end snaps to a node within this distance, else an Anchor is made (DESIGN §5.10).</summary>
    public const double RecordedEdgeSnapDistance = 3.0;
    public const int MaxWorldLength = 64;

    private static readonly Regex MaterialKey = new("^[A-Z0-9_]+$", RegexOptions.Compiled);

    private readonly IRoadNetworkRepository _repo;
    private readonly IMapper _mapper;

    public RoadNetworkService(IRoadNetworkRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    // ---------------------------------------------------------------- Tiles

    public async Task<List<RoadTileDto>> ListTilesAsync(string world)
    {
        RequireWorld(world);
        return _mapper.Map<List<RoadTileDto>>(await _repo.ListTilesAsync(world));
    }

    public async Task<RoadTileGraphDto?> GetTileGraphAsync(string world, int tileX, int tileZ)
    {
        RequireWorld(world);
        var tile = await _repo.GetTileAsync(world, tileX, tileZ);
        if (tile == null)
        {
            return null;
        }
        return new RoadTileGraphDto
        {
            Tile = _mapper.Map<RoadTileDto>(tile),
            Nodes = _mapper.Map<List<RoadNodeDto>>(await _repo.GetTileNodesAsync(tile.Id)),
            Edges = _mapper.Map<List<RoadEdgeDto>>(await _repo.GetTileEdgesAsync(tile.Id))
        };
    }

    public async Task<RoadTileDto> MarkTileDirtyAsync(string world, int tileX, int tileZ)
    {
        RequireWorld(world);
        var tile = await _repo.GetTileAsync(world, tileX, tileZ)
                   ?? await _repo.AddTileAsync(new RoadTile { World = world, TileX = tileX, TileZ = tileZ });
        if (!tile.Dirty)
        {
            tile.Dirty = true;
            tile.Version++;
            foreach (var edge in await _repo.GetTileEdgesAsync(tile.Id))
            {
                edge.Status = RoadEdgeStatus.Stale;
            }
            await _repo.SaveChangesAsync();
        }
        return _mapper.Map<RoadTileDto>(tile);
    }

    public Task<RoadTileUpsertResultDto> UpsertTileGraphAsync(string world, int tileX, int tileZ, RoadTileGraphUpsertDto dto)
    {
        RequireWorld(world);
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        return _repo.RunInTransactionAsync(() => UpsertTileGraphCoreAsync(world, tileX, tileZ, dto));
    }

    private sealed record TileBounds(int MinX, int MinZ, int MaxX, int MaxZ)
    {
        public bool Contains(int x, int z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
        public bool OnBorder(int x, int z) => Contains(x, z) && (x == MinX || x == MaxX || z == MinZ || z == MaxZ);
    }

    private static TileBounds BoundsOf(int tileX, int tileZ) =>
        new(tileX * RoadTile.Size, tileZ * RoadTile.Size, tileX * RoadTile.Size + RoadTile.Size - 1, tileZ * RoadTile.Size + RoadTile.Size - 1);

    private async Task<RoadTileUpsertResultDto> UpsertTileGraphCoreAsync(string world, int tileX, int tileZ, RoadTileGraphUpsertDto dto)
    {
        var now = DateTime.UtcNow;
        var bounds = BoundsOf(tileX, tileZ);
        var tile = await _repo.GetTileAsync(world, tileX, tileZ)
                   ?? await _repo.AddTileAsync(new RoadTile { World = world, TileX = tileX, TileZ = tileZ });
        await _repo.LockTileAsync(tile.Id);
        var result = new RoadTileUpsertResultDto();
        var bumped = new HashSet<int>();

        // 1. What the tile holds now.
        var existingNodes = await _repo.GetTileNodesAsync(tile.Id);
        var existingNodesById = existingNodes.ToDictionary(n => n.Id);
        var ownedEdges = await _repo.GetTileEdgesAsync(tile.Id);
        var touchingEdges = await _repo.GetEdgesTouchingNodesAsync(existingNodesById.Keys);
        var edgesById = ownedEdges.Concat(touchingEdges).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());

        // Nodes of other tiles referenced as "id:<n>".
        var foreignNodes = await LoadForeignNodesAsync(dto, world, existingNodesById);

        // 2. Validate before touching anything.
        await ValidatePayloadAsync(dto, bounds, existingNodesById, foreignNodes);

        // 3. Nodes: match by existingId, else by exact position; insert the rest.
        var nodeByKey = new Dictionary<string, RoadNode>(StringComparer.Ordinal);
        var matchedNodeIds = new HashSet<int>();
        var byPosition = existingNodes.ToDictionary(n => (n.X, n.Y, n.Z));
        foreach (var node in dto.Nodes)
        {
            RoadNode? target = null;
            if (node.ExistingId is int existingId)
            {
                target = existingNodesById[existingId];
            }
            else if (byPosition.TryGetValue((node.X, node.Y, node.Z), out var samePlace) && !matchedNodeIds.Contains(samePlace.Id))
            {
                target = samePlace;
            }

            if (target == null)
            {
                target = new RoadNode
                {
                    World = world, X = node.X, Y = node.Y, Z = node.Z, TileId = tile.Id,
                    Kind = node.Kind, Source = RoadNodeSource.Detected
                };
                _repo.Add(target);
                result.NodesCreated++;
            }
            else
            {
                if (!matchedNodeIds.Add(target.Id))
                {
                    throw new ArgumentException($"Node {target.Id} is matched by two payload nodes.");
                }
                if (!target.Locked)
                {
                    target.X = node.X; target.Y = node.Y; target.Z = node.Z;
                }
                if (target.Source == RoadNodeSource.Detected)
                {
                    target.Kind = node.Kind;
                }
                result.NodesUpdated++;
            }
            nodeByKey[node.Key] = target;
        }

        // Unmatched nodes: delete Detected ones unless an admin made, locked or recorded through them.
        var keptUnmatched = new List<RoadNode>();
        var deletedNodes = new List<RoadNode>();
        foreach (var node in existingNodes.Where(n => !matchedNodeIds.Contains(n.Id)))
        {
            var recordedThrough = touchingEdges.Any(e => e.Source == RoadEdgeSource.Recorded && (e.FromNodeId == node.Id || e.ToNodeId == node.Id));
            if (node.Source == RoadNodeSource.Manual || node.Locked || recordedThrough)
            {
                keptUnmatched.Add(node);
            }
            else
            {
                deletedNodes.Add(node);
            }
        }

        var finalPositions = new HashSet<(int, int, int)>();
        foreach (var node in nodeByKey.Values.Concat(keptUnmatched))
        {
            if (!finalPositions.Add((node.X, node.Y, node.Z)))
            {
                throw new ArgumentException($"Two nodes of the tile would share position ({node.X}, {node.Y}, {node.Z}).");
            }
        }

        // Delete the edges of deleted nodes explicitly (the InMemory provider only cascades to
        // tracked entities), remembering the tiles whose stitch edges go with them.
        var deletedNodeIds = deletedNodes.Select(n => n.Id).ToHashSet();
        var removedEdgeIds = new HashSet<int>();
        foreach (var edge in edgesById.Values.Where(e => deletedNodeIds.Contains(e.FromNodeId) || deletedNodeIds.Contains(e.ToNodeId)))
        {
            if (edge.TileId != tile.Id) bumped.Add(edge.TileId);
            _repo.Remove(edge);
            removedEdgeIds.Add(edge.Id);
            result.EdgesDeleted++;
        }
        result.DeletedNodes = _mapper.Map<List<RoadNodeDto>>(deletedNodes);
        result.NodesDeleted = deletedNodes.Count;
        _repo.RemoveRange(deletedNodes);
        await _repo.SaveChangesAsync(); // new nodes get their ids here

        // 4. Edges: match by existingId, else by node pair; insert the rest; delete unmatched detected ones.
        var pairIndex = new Dictionary<(int, int), RoadEdge>();
        foreach (var edge in edgesById.Values.Where(e => !removedEdgeIds.Contains(e.Id)))
        {
            pairIndex[(edge.FromNodeId, edge.ToNodeId)] = edge;
        }
        var ownedDetected = ownedEdges.Where(e => e.Source == RoadEdgeSource.Detected && !removedEdgeIds.Contains(e.Id)).ToDictionary(e => e.Id);
        var matchedEdgeIds = new HashSet<int>();
        var newEdges = new List<RoadEdge>();

        foreach (var edge in dto.Edges)
        {
            var from = ResolveNode(edge.FromKey, nodeByKey, foreignNodes);
            var to = ResolveNode(edge.ToKey, nodeByKey, foreignNodes);
            if (from.Id == to.Id)
            {
                throw new ArgumentException($"Edge {edge.FromKey}-{edge.ToKey} is a self-loop.");
            }
            var geometry = edge.Geometry;
            if (from.Id > to.Id)
            {
                (from, to) = (to, from);
                geometry = RoadGeometry.Reversed(geometry);
            }
            var pair = (from.Id, to.Id);

            RoadEdge? target = null;
            if (edge.ExistingId is int existingEdgeId)
            {
                if (!ownedDetected.TryGetValue(existingEdgeId, out target))
                {
                    throw new ArgumentException($"Edge {existingEdgeId} is not a detected edge of this tile.");
                }
            }
            else if (pairIndex.TryGetValue(pair, out var samePair))
            {
                if (samePair.TileId == tile.Id && samePair.Source == RoadEdgeSource.Detected && !matchedEdgeIds.Contains(samePair.Id))
                {
                    target = samePair;
                }
                else
                {
                    throw new ArgumentException($"Nodes {from.Id} and {to.Id} are already joined by {samePair.Source} edge {samePair.Id}.");
                }
            }
            if (target != null && pairIndex.TryGetValue(pair, out var occupant) && occupant.Id != target.Id)
            {
                throw new ArgumentException($"Nodes {from.Id} and {to.Id} are already joined by {occupant.Source} edge {occupant.Id}.");
            }
            if (target != null && !matchedEdgeIds.Add(target.Id))
            {
                throw new ArgumentException($"Edge {target.Id} is matched by two payload edges.");
            }

            if (target == null)
            {
                target = new RoadEdge { World = world, TileId = tile.Id, Source = RoadEdgeSource.Detected };
                newEdges.Add(target);
                result.EdgesCreated++;
            }
            else
            {
                pairIndex.Remove((target.FromNodeId, target.ToNodeId));
                result.EdgesUpdated++;
            }
            ApplyDetectedGeometry(target, from, to, geometry, edge);
            pairIndex[pair] = target;
        }

        foreach (var edge in ownedDetected.Values.Where(e => !matchedEdgeIds.Contains(e.Id)))
        {
            _repo.Remove(edge);
            result.EdgesDeleted++;
        }
        _repo.AddRange(newEdges);
        await _repo.SaveChangesAsync();

        // 5. Stitch edges (plan D7): every stitch touching this tile's boundary nodes is replaced,
        // owned by this tile; other tiles that lost one get a new Version.
        var tileNodes = nodeByKey.Values.Concat(keptUnmatched).GroupBy(n => n.Id).Select(g => g.First()).ToList();
        var boundaryNodes = tileNodes.Where(n => n.Kind == RoadNodeKind.Boundary).ToList();
        var oldStitches = (await _repo.GetEdgesTouchingNodesAsync(boundaryNodes.Select(n => n.Id)))
            .Concat(await _repo.GetTileEdgesAsync(tile.Id))
            .Where(e => e.Source == RoadEdgeSource.Stitch)
            .GroupBy(e => e.Id).Select(g => g.First()).ToList();
        foreach (var stitch in oldStitches)
        {
            if (stitch.TileId != tile.Id) bumped.Add(stitch.TileId);
            _repo.Remove(stitch);
        }
        await _repo.SaveChangesAsync();

        var remainingPairs = (await _repo.GetEdgesTouchingNodesAsync(boundaryNodes.Select(n => n.Id)))
            .Select(e => (e.FromNodeId, e.ToNodeId)).ToHashSet();
        var neighbourNodes = (await _repo.GetNodesInBoxAsync(world, bounds.MinX - 1, bounds.MinZ - 1, bounds.MaxX + 1, bounds.MaxZ + 1))
            .Where(n => n.TileId != tile.Id && n.Kind == RoadNodeKind.Boundary).ToList();
        var stitches = new List<RoadEdge>();
        foreach (var mine in boundaryNodes)
        {
            foreach (var theirs in neighbourNodes)
            {
                if (Math.Abs(mine.X - theirs.X) > 1 || Math.Abs(mine.Z - theirs.Z) > 1 || Math.Abs(mine.Y - theirs.Y) > 1)
                {
                    continue;
                }
                var (a, b) = mine.Id < theirs.Id ? (mine, theirs) : (theirs, mine);
                if (!remainingPairs.Add((a.Id, b.Id)))
                {
                    continue;
                }
                var geometry = new[] { new[] { a.X, a.Y, a.Z }, new[] { b.X, b.Y, b.Z } };
                stitches.Add(new RoadEdge
                {
                    World = world, TileId = tile.Id, Source = RoadEdgeSource.Stitch,
                    FromNodeId = a.Id, ToNodeId = b.Id,
                    GeometryJson = RoadJson.GeometryJson(geometry),
                    Length = RoadGeometry.Distance(geometry[0], geometry[1]),
                    MinX = Math.Min(a.X, b.X), MinY = Math.Min(a.Y, b.Y), MinZ = Math.Min(a.Z, b.Z),
                    MaxX = Math.Max(a.X, b.X), MaxY = Math.Max(a.Y, b.Y), MaxZ = Math.Max(a.Z, b.Z),
                    AvgWidth = 1.0
                });
            }
        }
        _repo.AddRange(stitches);
        result.StitchEdges = stitches.Count;
        await _repo.SaveChangesAsync();

        // 6. Street labels (plan D6) for the tile's edges, with the neighbouring ring for continuation.
        var tileEdges = await _repo.GetTileEdgesAsync(tile.Id);
        var ringEdges = (await _repo.GetEdgesInBoxAsync(world, bounds.MinX - RoadTile.Size, bounds.MinZ - RoadTile.Size, bounds.MaxX + RoadTile.Size, bounds.MaxZ + RoadTile.Size))
            .Where(e => e.TileId != tile.Id).ToList();
        var structures = await _repo.GetStructuresWithLocationInBoxAsync(world,
            bounds.MinX - (int)RoadStreetLabeler.VoteRadius, bounds.MinZ - (int)RoadStreetLabeler.VoteRadius,
            bounds.MaxX + (int)RoadStreetLabeler.VoteRadius, bounds.MaxZ + (int)RoadStreetLabeler.VoteRadius);
        var classes = await _repo.GetProfileClassesAsync();
        var labels = RoadStreetLabeler.Label(
            tileEdges.Concat(ringEdges).Select(e => ToLabelerEdge(e, classes)).ToList(),
            structures,
            tileEdges.Select(e => e.Id).ToHashSet());
        foreach (var edge in tileEdges)
        {
            edge.Status = RoadEdgeStatus.Ok;
            if (edge.StreetSource == RoadStreetSource.Manual)
            {
                result.LabelledEdges++;
                continue;
            }
            var street = labels.Labels.GetValueOrDefault(edge.Id);
            edge.StreetId = street;
            edge.StreetSource = street == null ? RoadStreetSource.None : RoadStreetSource.Inferred;
            if (street == null) result.UnlabelledEdges++; else result.LabelledEdges++;
        }
        result.Conflicts = labels.Conflicts;

        // 7. The tile itself, and the neighbours that lost a stitch edge.
        tile.Version++;
        tile.BuiltAt = now;
        tile.BuilderVersion = dto.BuilderVersion;
        tile.Dirty = false;
        tile.CellCount = dto.CellCount;
        tile.LevelCount = dto.LevelCount;
        tile.NodeCount = tileNodes.Count;
        tile.EdgeCount = tileEdges.Count;
        tile.WarningsJson = RoadJson.ListJson(dto.Warnings.Concat(labels.Conflicts));
        bumped.Remove(tile.Id);
        foreach (var other in await _repo.GetTilesByIdsAsync(bumped))
        {
            other.Version++;
            result.BumpedTileIds.Add(other.Id);
        }
        await _repo.SaveChangesAsync();

        // 8. Components for the whole world.
        await RecomputeComponentsAsync(world);
        await _repo.SaveChangesAsync();

        result.Tile = _mapper.Map<RoadTileDto>(tile);
        return result;
    }

    private async Task<Dictionary<int, RoadNode>> LoadForeignNodesAsync(RoadTileGraphUpsertDto dto, string world, Dictionary<int, RoadNode> ownNodes)
    {
        var ids = new HashSet<int>();
        foreach (var key in dto.Edges.SelectMany(e => new[] { e.FromKey, e.ToKey }))
        {
            if (TryParseIdKey(key, out var id))
            {
                ids.Add(id);
            }
        }
        var foreign = new Dictionary<int, RoadNode>();
        foreach (var id in ids)
        {
            if (ownNodes.TryGetValue(id, out var own))
            {
                foreign[id] = own;
            }
        }
        foreach (var node in await _repo.GetNodesByIdsAsync(ids.Where(id => !foreign.ContainsKey(id))))
        {
            if (node.World != world)
            {
                throw new ArgumentException($"Node {node.Id} is in world '{node.World}', not '{world}'.");
            }
            foreign[node.Id] = node;
        }
        foreach (var id in ids)
        {
            if (!foreign.ContainsKey(id))
            {
                throw new ArgumentException($"Node id:{id} does not exist.");
            }
        }
        return foreign;
    }

    private static bool TryParseIdKey(string? key, out int id)
    {
        id = 0;
        return key != null && key.StartsWith("id:", StringComparison.Ordinal) && int.TryParse(key.AsSpan(3), out id);
    }

    private async Task ValidatePayloadAsync(RoadTileGraphUpsertDto dto, TileBounds bounds, Dictionary<int, RoadNode> existingNodes, Dictionary<int, RoadNode> foreignNodes)
    {
        if (dto.BuilderVersion < 0) throw new ArgumentException("builderVersion must be >= 0.");
        if (dto.CellCount < 0 || dto.LevelCount < 0) throw new ArgumentException("cellCount and levelCount must be >= 0.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var positions = new HashSet<(int, int, int)>();
        var claimedIds = new HashSet<int>();
        var payloadNodes = new Dictionary<string, RoadTileGraphNodeDto>(StringComparer.Ordinal);
        foreach (var node in dto.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Key)) throw new ArgumentException("Every node needs a key.");
            if (TryParseIdKey(node.Key, out _)) throw new ArgumentException($"Node key '{node.Key}' uses the reserved id: prefix.");
            if (!keys.Add(node.Key)) throw new ArgumentException($"Node key '{node.Key}' is used twice.");
            if (!Enum.IsDefined(node.Kind)) throw new ArgumentException($"Node '{node.Key}' has an unknown kind.");
            if (!bounds.Contains(node.X, node.Z))
            {
                throw new ArgumentException($"Node '{node.Key}' at ({node.X}, {node.Z}) is outside the tile (x {bounds.MinX}..{bounds.MaxX}, z {bounds.MinZ}..{bounds.MaxZ}).");
            }
            if (node.Kind == RoadNodeKind.Boundary && !bounds.OnBorder(node.X, node.Z))
            {
                throw new ArgumentException($"Boundary node '{node.Key}' at ({node.X}, {node.Z}) is not on the tile border.");
            }
            if (!positions.Add((node.X, node.Y, node.Z))) throw new ArgumentException($"Two payload nodes share position ({node.X}, {node.Y}, {node.Z}).");
            if (node.ExistingId is int existingId)
            {
                if (!existingNodes.ContainsKey(existingId)) throw new ArgumentException($"Node '{node.Key}' references node {existingId}, which is not a node of this tile.");
                if (!claimedIds.Add(existingId)) throw new ArgumentException($"Node {existingId} is referenced by two payload nodes.");
            }
            payloadNodes[node.Key] = node;
        }

        var pairs = new HashSet<(string, string)>();
        var claimedEdgeIds = new HashSet<int>();
        foreach (var edge in dto.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.FromKey) || string.IsNullOrWhiteSpace(edge.ToKey)) throw new ArgumentException("Every edge needs fromKey and toKey.");
            if (edge.FromKey == edge.ToKey) throw new ArgumentException($"Edge {edge.FromKey}-{edge.ToKey} is a self-loop.");
            var from = EndPosition(edge.FromKey, payloadNodes, foreignNodes);
            var to = EndPosition(edge.ToKey, payloadNodes, foreignNodes);
            var pair = string.CompareOrdinal(edge.FromKey, edge.ToKey) < 0 ? (edge.FromKey, edge.ToKey) : (edge.ToKey, edge.FromKey);
            if (!pairs.Add(pair)) throw new ArgumentException($"Edge {edge.FromKey}-{edge.ToKey} appears twice.");
            if (edge.ExistingId is int existingEdgeId && !claimedEdgeIds.Add(existingEdgeId)) throw new ArgumentException($"Edge {existingEdgeId} is referenced by two payload edges.");

            ValidateGeometry(edge.Geometry, edge.Length, from, to, $"Edge {edge.FromKey}-{edge.ToKey}");
            if (edge.AvgWidth < 0) throw new ArgumentException($"Edge {edge.FromKey}-{edge.ToKey}: avgWidth must be >= 0.");
            if (edge.ProfileId is int profileId && await _repo.GetProfileAsync(profileId) == null)
            {
                throw new ArgumentException($"Edge {edge.FromKey}-{edge.ToKey}: profile {profileId} does not exist.");
            }
        }
    }

    private static int[] EndPosition(string key, Dictionary<string, RoadTileGraphNodeDto> payloadNodes, Dictionary<int, RoadNode> foreignNodes)
    {
        if (payloadNodes.TryGetValue(key, out var node))
        {
            return new[] { node.X, node.Y, node.Z };
        }
        if (TryParseIdKey(key, out var id) && foreignNodes.TryGetValue(id, out var existing))
        {
            return new[] { existing.X, existing.Y, existing.Z };
        }
        throw new ArgumentException($"Edge references unknown node key '{key}'.");
    }

    private static void ValidateGeometry(int[][] geometry, double length, int[] from, int[] to, string what)
    {
        if (!RoadGeometry.IsWellFormed(geometry) || geometry.Length < 2)
        {
            throw new ArgumentException($"{what}: geometry needs at least two [x, y, z] points.");
        }
        if (RoadGeometry.Distance(geometry[0], from) > GeometryEndTolerance || RoadGeometry.Distance(geometry[^1], to) > GeometryEndTolerance)
        {
            throw new ArgumentException($"{what}: geometry must start and end within {GeometryEndTolerance} blocks of its nodes.");
        }
        var straight = RoadGeometry.Distance(from, to);
        if (length <= 0 || length + 1e-6 < straight)
        {
            throw new ArgumentException($"{what}: length {length:0.##} must be at least the straight-line distance {straight:0.##}.");
        }
    }

    private static RoadNode ResolveNode(string key, Dictionary<string, RoadNode> nodeByKey, Dictionary<int, RoadNode> foreignNodes)
    {
        if (nodeByKey.TryGetValue(key, out var node)) return node;
        if (TryParseIdKey(key, out var id) && foreignNodes.TryGetValue(id, out var existing)) return existing;
        throw new ArgumentException($"Edge references unknown node key '{key}'.");
    }

    private static void ApplyDetectedGeometry(RoadEdge target, RoadNode from, RoadNode to, int[][] geometry, RoadTileGraphEdgeDto dto)
    {
        target.FromNodeId = from.Id;
        target.ToNodeId = to.Id;
        target.GeometryJson = RoadJson.GeometryJson(geometry);
        target.Length = dto.Length;
        target.AvgWidth = dto.AvgWidth;
        var box = RoadGeometry.BoundingBox(geometry);
        (target.MinX, target.MinY, target.MinZ, target.MaxX, target.MaxY, target.MaxZ) = box;
        target.ProfileId = dto.ProfileId;
        target.GateDoorIdsJson = RoadJson.ListJson(dto.GateDoorIds);
        target.DomainIdsJson = RoadJson.ListJson(dto.DomainIds);
        target.RegionIdsJson = RoadJson.ListJson(dto.RegionIds);
        target.Status = RoadEdgeStatus.Ok;
        // StreetId/StreetSource (when Manual), Flags and CostMultiplier are kept as they are.
    }

    private static RoadStreetLabeler.Edge ToLabelerEdge(RoadEdge edge, Dictionary<int, RoadClass> classes) => new()
    {
        Id = edge.Id,
        FromNodeId = edge.FromNodeId,
        ToNodeId = edge.ToNodeId,
        Geometry = RoadJson.Geometry(edge.GeometryJson),
        RoadClass = edge.ProfileId is int profileId && classes.TryGetValue(profileId, out var roadClass) ? roadClass : null,
        StreetId = edge.StreetId,
        StreetSource = edge.StreetSource
    };

    private async Task RecomputeComponentsAsync(string world)
    {
        var nodes = await _repo.GetWorldNodesAsync(world);
        var edges = await _repo.GetWorldEdgesAsync(world);
        var components = RoadComponents.Compute(nodes.Select(n => n.Id), edges.Select(e => (e.FromNodeId, e.ToNodeId)));
        foreach (var node in nodes)
        {
            var component = components[node.Id];
            if (node.ComponentId != component)
            {
                node.ComponentId = component;
            }
        }
    }

    // -------------------------------------------------------------- Network

    public async Task<RoadNetworkMetaDto> GetMetaAsync(string world)
    {
        RequireWorld(world);
        var streetNames = await _repo.GetStreetNamesAsync(await _repo.GetLabelledStreetIdsAsync(world));
        var nodes = await _repo.GetWorldNodesAsync(world);
        return new RoadNetworkMetaDto
        {
            Profiles = _mapper.Map<List<RoadProfileDto>>(await _repo.ListProfilesAsync()),
            Streets = streetNames.OrderBy(s => s.Key).Select(s => new RoadStreetRefDto { Id = s.Key, Name = s.Value }).ToList(),
            Components = nodes.GroupBy(n => n.ComponentId).OrderBy(g => g.Key)
                .Select(g => new RoadComponentDto { Id = g.Key, NodeCount = g.Count() }).ToList()
        };
    }

    public Task<List<RoadSeedLocationDto>> GetSeedLocationsAsync(string world, int minX, int minZ, int maxX, int maxZ)
    {
        RequireWorld(world);
        if (minX > maxX || minZ > maxZ) throw new ArgumentException("min must not exceed max.");
        return _repo.GetDomainLocationsInBoxAsync(world, minX, minZ, maxX, maxZ);
    }

    // ------------------------------------------------------------- Profiles

    public async Task<List<RoadProfileDto>> ListProfilesAsync() =>
        _mapper.Map<List<RoadProfileDto>>(await _repo.ListProfilesAsync());

    public async Task<RoadProfileDto?> GetProfileAsync(int id)
    {
        var profile = await _repo.GetProfileAsync(id);
        return profile == null ? null : _mapper.Map<RoadProfileDto>(profile);
    }

    public async Task<RoadProfileDto> CreateProfileAsync(RoadProfileUpsertDto dto)
    {
        await ValidateProfileAsync(dto, null);
        var profile = new RoadProfile { StatsJson = RoadJson.ElementJson(dto.Stats) };
        ApplyProfile(profile, dto);
        _repo.Add(profile);
        await _repo.SaveChangesAsync();
        return _mapper.Map<RoadProfileDto>(profile);
    }

    public async Task<RoadProfileDto> UpdateProfileAsync(int id, RoadProfileUpsertDto dto)
    {
        var profile = await _repo.GetProfileAsync(id) ?? throw new KeyNotFoundException($"Road profile {id} not found.");
        await ValidateProfileAsync(dto, id);
        ApplyProfile(profile, dto);
        if (dto.Stats != null)
        {
            profile.StatsJson = RoadJson.ElementJson(dto.Stats);
        }
        await _repo.SaveChangesAsync();
        return _mapper.Map<RoadProfileDto>(profile);
    }

    public async Task<bool> DeleteProfileAsync(int id)
    {
        var profile = await _repo.GetProfileAsync(id);
        if (profile == null)
        {
            return false;
        }
        // SetNull explicitly, so the in-memory provider matches MySQL's FK behaviour.
        foreach (var edge in await _repo.GetEdgesByProfileAsync(id))
        {
            edge.ProfileId = null;
        }
        foreach (var survey in await _repo.GetSurveysByProfileAsync(id))
        {
            survey.ProfileId = null;
        }
        _repo.Remove(profile);
        await _repo.SaveChangesAsync();
        return true;
    }

    private async Task ValidateProfileAsync(RoadProfileUpsertDto dto, int? currentId)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Profile name is required.");
        if (dto.Name.Trim().Length > 100) throw new ArgumentException("Profile name is at most 100 characters.");
        var existing = await _repo.GetProfileByNameAsync(dto.Name.Trim());
        if (existing != null && existing.Id != currentId) throw new ArgumentException($"A road profile named '{dto.Name.Trim()}' already exists.");
        if (!Enum.IsDefined(dto.RoadClass)) throw new ArgumentException("Unknown road class.");
        if (dto.CostMultiplier <= 0) throw new ArgumentException("costMultiplier must be > 0.");
        if (dto.WidthMin < 1 || dto.WidthMax < dto.WidthMin) throw new ArgumentException("widthMin must be >= 1 and <= widthMax.");
        if (dto.SampleCount < 0) throw new ArgumentException("sampleCount must be >= 0.");

        var materials = new HashSet<string>(StringComparer.Ordinal);
        foreach (var material in dto.Materials ?? new List<RoadMaterialDto>())
        {
            if (material == null || string.IsNullOrWhiteSpace(material.Material) || !MaterialKey.IsMatch(material.Material))
            {
                throw new ArgumentException($"Material key '{material?.Material}' must match ^[A-Z0-9_]+$ (a Bukkit Material name).");
            }
            if (!Enum.IsDefined(material.Role)) throw new ArgumentException($"Material {material.Material} has an unknown role.");
            if (!materials.Add(material.Material)) throw new ArgumentException($"Material {material.Material} is listed twice.");
            if (material.CentreShare < 0 || material.EdgeShare < 0 || material.Samples < 0) throw new ArgumentException($"Material {material.Material}: shares and samples must be >= 0.");
        }

        if (dto.ScopeTownIds != null && dto.ScopeTownIds.Count > 0)
        {
            var towns = (await _repo.GetExistingTownIdsAsync(dto.ScopeTownIds)).ToHashSet();
            var missing = dto.ScopeTownIds.Where(id => !towns.Contains(id)).Distinct().ToList();
            if (missing.Count > 0) throw new ArgumentException($"scopeTownIds must be Towns; unknown: {string.Join(", ", missing)}.");
        }
    }

    private static void ApplyProfile(RoadProfile profile, RoadProfileUpsertDto dto)
    {
        profile.Name = dto.Name.Trim();
        profile.RoadClass = dto.RoadClass;
        profile.CostMultiplier = dto.CostMultiplier;
        profile.MaterialsJson = JsonColumn.Serialize(dto.Materials ?? new List<RoadMaterialDto>());
        profile.WidthMin = dto.WidthMin;
        profile.WidthMax = dto.WidthMax;
        profile.SampleCount = dto.SampleCount;
        profile.Enabled = dto.Enabled;
        profile.ScopeTownIdsJson = dto.ScopeTownIds == null ? null : RoadJson.ListJson(dto.ScopeTownIds.Distinct());
        profile.UpdatedAt = DateTime.UtcNow;
    }

    // -------------------------------------------------------------- Surveys

    public async Task<RoadSurveyDto> CreateSurveyAsync(RoadSurveyCreateDto dto, int? startedByUserId)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        RequireWorld(dto.World);
        if (dto.StartedAt == default) throw new ArgumentException("startedAt is required.");
        if (dto.EndedAt is DateTime ended && ended < dto.StartedAt) throw new ArgumentException("endedAt must not be before startedAt.");
        if (dto.SampleCount < 0) throw new ArgumentException("sampleCount must be >= 0.");
        if (dto.ProfileId is int profileId && await _repo.GetProfileAsync(profileId) == null)
        {
            throw new ArgumentException($"Profile {profileId} does not exist.");
        }
        var survey = new RoadSurvey
        {
            World = dto.World,
            ProfileId = dto.ProfileId,
            StartedByUserId = startedByUserId,
            StartedAt = dto.StartedAt,
            EndedAt = dto.EndedAt,
            SampleCount = dto.SampleCount,
            BreadcrumbJson = JsonColumn.Serialize(dto.Breadcrumb ?? new List<RoadBreadcrumbPointDto>()),
            StatsJson = RoadJson.ElementJson(dto.Stats)
        };
        _repo.Add(survey);
        await _repo.SaveChangesAsync();
        return _mapper.Map<RoadSurveyDto>(survey);
    }

    public async Task<List<RoadSurveyDto>> ListSurveysAsync(string world)
    {
        RequireWorld(world);
        return _mapper.Map<List<RoadSurveyDto>>(await _repo.ListSurveysAsync(world));
    }

    // ---------------------------------------------------------------- Seeds

    public async Task<List<RoadSeedDto>> ListSeedsAsync(string world)
    {
        RequireWorld(world);
        return _mapper.Map<List<RoadSeedDto>>(await _repo.ListSeedsAsync(world));
    }

    public async Task<RoadSeedDto> CreateSeedAsync(RoadSeedCreateDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        RequireWorld(dto.World);
        if (!Enum.IsDefined(dto.Source)) throw new ArgumentException("Unknown seed source.");
        if (dto.Note != null && dto.Note.Length > 200) throw new ArgumentException("note is at most 200 characters.");
        if (dto.SurveyId is int surveyId && !await _repo.SurveyExistsAsync(surveyId)) throw new ArgumentException($"Survey {surveyId} does not exist.");
        var seed = new RoadSeed
        {
            World = dto.World, X = dto.X, Y = dto.Y, Z = dto.Z,
            Source = dto.Source, SurveyId = dto.SurveyId, Note = dto.Note
        };
        _repo.Add(seed);
        await _repo.SaveChangesAsync();
        return _mapper.Map<RoadSeedDto>(seed);
    }

    public async Task<bool> DeleteSeedAsync(int id)
    {
        var seed = await _repo.GetSeedAsync(id);
        if (seed == null)
        {
            return false;
        }
        _repo.Remove(seed);
        await _repo.SaveChangesAsync();
        return true;
    }

    // ---------------------------------------------------------------- Nodes

    public async Task<RoadNodeDto> UpdateNodeAsync(int id, RoadNodeUpdateDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        var node = await _repo.GetNodeAsync(id) ?? throw new KeyNotFoundException($"Road node {id} not found.");
        var edited = false;
        if (dto.ClearName)
        {
            node.Name = null;
            edited = true;
        }
        else if (dto.Name != null)
        {
            var name = dto.Name.Trim();
            if (name.Length > 100) throw new ArgumentException("name is at most 100 characters.");
            node.Name = name.Length == 0 ? null : name;
            edited = true;
        }
        if (dto.Kind is RoadNodeKind kind)
        {
            if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown node kind.");
            if (kind == RoadNodeKind.Boundary)
            {
                var tile = await _repo.GetTileByIdAsync(node.TileId) ?? throw new KeyNotFoundException($"Tile {node.TileId} not found.");
                if (!BoundsOf(tile.TileX, tile.TileZ).OnBorder(node.X, node.Z)) throw new ArgumentException("Only a node on the tile border can be a Boundary node.");
            }
            node.Kind = kind;
            edited = true;
        }
        // An admin edit locks the node so rebuilds keep it (DESIGN §3.5), unless told otherwise.
        node.Locked = dto.Locked ?? (edited || node.Locked);
        await BumpTileAsync(node.TileId);
        await _repo.SaveChangesAsync();
        return _mapper.Map<RoadNodeDto>(node);
    }

    public async Task<RoadNodeDto> CreateAnchorAsync(RoadNodeAnchorDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        RequireWorld(dto.World);
        if (dto.Name != null && dto.Name.Trim().Length > 100) throw new ArgumentException("name is at most 100 characters.");
        if (await _repo.GetNodeAtAsync(dto.World, dto.X, dto.Y, dto.Z) is RoadNode occupied)
        {
            throw new InvalidOperationException($"Node {occupied.Id} already sits at ({dto.X}, {dto.Y}, {dto.Z}).");
        }
        var node = await NewManualAnchorAsync(dto.World, dto.X, dto.Y, dto.Z, string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name.Trim());
        _repo.Add(node);
        await _repo.SaveChangesAsync();
        node.ComponentId = node.Id; // isolated until an edge joins it
        await _repo.SaveChangesAsync();
        return _mapper.Map<RoadNodeDto>(node);
    }

    private async Task<RoadNode> NewManualAnchorAsync(string world, int x, int y, int z, string? name)
    {
        var tile = await GetOrCreateTileAtAsync(world, x, z);
        tile.Version++;
        return new RoadNode
        {
            World = world, X = x, Y = y, Z = z, TileId = tile.Id,
            Kind = RoadNodeKind.Anchor, Source = RoadNodeSource.Manual, Locked = true, Name = name
        };
    }

    private async Task<RoadTile> GetOrCreateTileAtAsync(string world, int x, int z)
    {
        var tileX = RoadTile.TileCoordinate(x);
        var tileZ = RoadTile.TileCoordinate(z);
        return await _repo.GetTileAsync(world, tileX, tileZ)
               ?? await _repo.AddTileAsync(new RoadTile { World = world, TileX = tileX, TileZ = tileZ });
    }

    public async Task<RoadNodeDto> MergeNodesAsync(RoadNodeMergeDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (dto.KeepNodeId == dto.MergeNodeId) throw new ArgumentException("keepNodeId and mergeNodeId must differ.");
        var keep = await _repo.GetNodeAsync(dto.KeepNodeId) ?? throw new KeyNotFoundException($"Road node {dto.KeepNodeId} not found.");
        var merge = await _repo.GetNodeAsync(dto.MergeNodeId) ?? throw new KeyNotFoundException($"Road node {dto.MergeNodeId} not found.");
        if (keep.World != merge.World) throw new ArgumentException("Both nodes must be in the same world.");

        return await _repo.RunInTransactionAsync(async () =>
        {
            var touching = await _repo.GetEdgesTouchingNodesAsync(new[] { keep.Id, merge.Id });
            var pairs = touching.Where(e => e.FromNodeId != merge.Id && e.ToNodeId != merge.Id)
                .Select(e => (e.FromNodeId, e.ToNodeId)).ToHashSet();
            var bumped = new HashSet<int> { keep.TileId, merge.TileId };

            foreach (var edge in touching.Where(e => e.FromNodeId == merge.Id || e.ToNodeId == merge.Id))
            {
                bumped.Add(edge.TileId);
                var from = edge.FromNodeId == merge.Id ? keep.Id : edge.FromNodeId;
                var to = edge.ToNodeId == merge.Id ? keep.Id : edge.ToNodeId;
                if (from == to)
                {
                    _repo.Remove(edge); // self-loop
                    continue;
                }
                var geometry = RoadJson.Geometry(edge.GeometryJson);
                if (from > to)
                {
                    (from, to) = (to, from);
                    geometry = RoadGeometry.Reversed(geometry);
                }
                if (!pairs.Add((from, to)))
                {
                    _repo.Remove(edge); // duplicate of an edge the kept node already has
                    continue;
                }
                edge.FromNodeId = from;
                edge.ToNodeId = to;
                // Re-anchor the geometry end that pointed at the merged node (exactly one end is
                // the kept node now: a loop would have been removed above).
                if (geometry.Length > 0)
                {
                    geometry[from == keep.Id ? 0 : geometry.Length - 1] = new[] { keep.X, keep.Y, keep.Z };
                }
                edge.GeometryJson = RoadJson.GeometryJson(geometry);
                (edge.MinX, edge.MinY, edge.MinZ, edge.MaxX, edge.MaxY, edge.MaxZ) = RoadGeometry.BoundingBox(geometry);
            }
            await _repo.SaveChangesAsync();
            _repo.Remove(merge);
            keep.Locked = true;
            foreach (var tile in await _repo.GetTilesByIdsAsync(bumped))
            {
                tile.Version++;
            }
            await _repo.SaveChangesAsync();
            await RecomputeComponentsAsync(keep.World);
            await _repo.SaveChangesAsync();
            return _mapper.Map<RoadNodeDto>(keep);
        });
    }

    // ---------------------------------------------------------------- Edges

    public async Task<PagedResultDto<RoadEdgeDto>> SearchEdgesAsync(PagedQueryDto queryDto)
    {
        if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
        var result = await _repo.SearchEdgesAsync(_mapper.Map<PagedQuery>(queryDto));
        return new PagedResultDto<RoadEdgeDto>
        {
            Items = _mapper.Map<List<RoadEdgeDto>>(result.Items),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        };
    }

    public async Task<RoadEdgeDto> CreateRecordedEdgeAsync(RoadEdgeRecordDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        RequireWorld(dto.World);
        if (!RoadGeometry.IsWellFormed(dto.Geometry) || dto.Geometry.Length < 2)
        {
            throw new ArgumentException("geometry needs at least two [x, y, z] points.");
        }
        if (dto.AvgWidth < 0) throw new ArgumentException("avgWidth must be >= 0.");
        if (dto.ProfileId is int profileId && await _repo.GetProfileAsync(profileId) == null) throw new ArgumentException($"Profile {profileId} does not exist.");
        if (dto.StreetId is int streetId && !await _repo.StreetExistsAsync(streetId)) throw new ArgumentException($"Street {streetId} does not exist.");
        var polyline = RoadGeometry.PolylineLength(dto.Geometry);
        var length = dto.Length ?? polyline;
        var straight = RoadGeometry.Distance(dto.Geometry[0], dto.Geometry[^1]);
        if (length <= 0 || length + 1e-6 < straight) throw new ArgumentException($"length {length:0.##} must be at least the straight-line distance {straight:0.##}.");

        return await _repo.RunInTransactionAsync(async () =>
        {
            var from = await SnapOrAnchorAsync(dto.World, dto.Geometry[0]);
            var to = await SnapOrAnchorAsync(dto.World, dto.Geometry[^1], from);
            await _repo.SaveChangesAsync();
            if (from.Id == to.Id) throw new ArgumentException("Both ends snap to the same node.");

            var geometry = dto.Geometry;
            if (from.Id > to.Id)
            {
                (from, to) = (to, from);
                geometry = RoadGeometry.Reversed(geometry);
            }
            if ((await _repo.GetEdgesTouchingNodesAsync(new[] { from.Id })).Any(e => e.FromNodeId == from.Id && e.ToNodeId == to.Id))
            {
                throw new InvalidOperationException($"Nodes {from.Id} and {to.Id} are already joined by an edge.");
            }

            var tile = await GetOrCreateTileAtAsync(dto.World, dto.Geometry[0][0], dto.Geometry[0][2]);
            tile.Version++;
            // A detected node the recording snapped to is admin cleanup now: lock it, so the builder
            // keeps it and merges its rebuilt duplicates into it (smoke test fix plan 5.5 item 6).
            foreach (var end in new[] { from, to }.Where(n => !n.Locked))
            {
                end.Locked = true;
                if (end.TileId != tile.Id) await BumpTileAsync(end.TileId);
            }
            var box = RoadGeometry.BoundingBox(geometry);
            var edge = new RoadEdge
            {
                World = dto.World, TileId = tile.Id, Source = RoadEdgeSource.Recorded,
                FromNodeId = from.Id, ToNodeId = to.Id,
                GeometryJson = RoadJson.GeometryJson(geometry),
                Length = length, AvgWidth = dto.AvgWidth,
                MinX = box.minX, MinY = box.minY, MinZ = box.minZ, MaxX = box.maxX, MaxY = box.maxY, MaxZ = box.maxZ,
                ProfileId = dto.ProfileId,
                StreetId = dto.StreetId,
                StreetSource = dto.StreetId == null ? RoadStreetSource.None : RoadStreetSource.Manual,
                GateDoorIdsJson = RoadJson.ListJson(dto.GateDoorIds),
                DomainIdsJson = RoadJson.ListJson(dto.DomainIds),
                RegionIdsJson = RoadJson.ListJson(dto.RegionIds)
            };
            _repo.Add(edge);
            await _repo.SaveChangesAsync();
            await RecomputeComponentsAsync(dto.World);
            await _repo.SaveChangesAsync();
            return _mapper.Map<RoadEdgeDto>(edge);
        });
    }

    private async Task<RoadNode> SnapOrAnchorAsync(string world, int[] end, RoadNode? exclude = null)
    {
        var radius = (int)Math.Ceiling(RecordedEdgeSnapDistance);
        var candidates = await _repo.GetNodesInBoxAsync(world, end[0] - radius, end[2] - radius, end[0] + radius, end[2] + radius);
        var nearest = candidates
            .Where(n => exclude == null || n.Id != exclude.Id)
            .Select(n => (node: n, distance: RoadGeometry.Distance(end[0], end[1], end[2], n.X, n.Y, n.Z)))
            .Where(c => c.distance <= RecordedEdgeSnapDistance)
            .OrderBy(c => c.distance).ThenBy(c => c.node.Id)
            .Select(c => c.node)
            .FirstOrDefault();
        if (nearest != null)
        {
            return nearest;
        }
        if (exclude != null && exclude.X == end[0] && exclude.Y == end[1] && exclude.Z == end[2])
        {
            return exclude;
        }
        var anchor = await NewManualAnchorAsync(world, end[0], end[1], end[2], null);
        _repo.Add(anchor);
        return anchor;
    }

    public async Task<RoadEdgeUpdateResultDto> UpdateEdgeAsync(int id, RoadEdgeUpdateDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        var edge = await _repo.GetEdgeAsync(id) ?? throw new KeyNotFoundException($"Road edge {id} not found.");
        if (dto.StreetId != null && dto.ClearStreet) throw new ArgumentException("streetId and clearStreet exclude each other.");
        if (dto.ProfileId != null && dto.ClearProfile) throw new ArgumentException("profileId and clearProfile exclude each other.");
        if (dto.StreetId is int streetId && !await _repo.StreetExistsAsync(streetId)) throw new ArgumentException($"Street {streetId} does not exist.");
        if (dto.ProfileId is int profileId && await _repo.GetProfileAsync(profileId) == null) throw new ArgumentException($"Profile {profileId} does not exist.");
        if (dto.CostMultiplier is double cost && cost <= 0) throw new ArgumentException("costMultiplier must be > 0.");
        var flags = dto.Flags == null ? (RoadEdgeFlags?)null : RoadJson.ParseFlags(dto.Flags);

        return await _repo.RunInTransactionAsync(async () =>
        {
            var changed = new List<int> { edge.Id };
            var bumped = new HashSet<int> { edge.TileId };

            if (dto.ClearStreet)
            {
                edge.StreetId = null;
                edge.StreetSource = RoadStreetSource.None;
            }
            else if (dto.StreetId is int street)
            {
                edge.StreetId = street;
                edge.StreetSource = RoadStreetSource.Manual;
                if (dto.Propagate)
                {
                    var classes = await _repo.GetProfileClassesAsync();
                    var worldEdges = await _repo.GetWorldEdgesAsync(edge.World);
                    var byId = worldEdges.ToDictionary(e => e.Id);
                    foreach (var otherId in RoadStreetLabeler.Propagate(worldEdges.Select(e => ToLabelerEdge(e, classes)).ToList(), edge.Id, street))
                    {
                        var other = byId[otherId];
                        other.StreetId = street;
                        other.StreetSource = RoadStreetSource.Manual;
                        bumped.Add(other.TileId);
                        changed.Add(other.Id);
                    }
                }
            }
            if (dto.ClearProfile) edge.ProfileId = null;
            else if (dto.ProfileId is int profile) edge.ProfileId = profile;
            if (dto.CostMultiplier is double multiplier) edge.CostMultiplier = multiplier;
            if (flags is RoadEdgeFlags set) edge.Flags = set;

            foreach (var tile in await _repo.GetTilesByIdsAsync(bumped))
            {
                tile.Version++;
            }
            await _repo.SaveChangesAsync();
            return new RoadEdgeUpdateResultDto { Edge = _mapper.Map<RoadEdgeDto>(edge), ChangedEdgeIds = changed };
        });
    }

    public async Task<bool> DeleteEdgeAsync(int id)
    {
        var edge = await _repo.GetEdgeAsync(id);
        if (edge == null)
        {
            return false;
        }
        var world = edge.World;
        await BumpTileAsync(edge.TileId);
        _repo.Remove(edge);
        await _repo.SaveChangesAsync();
        await RecomputeComponentsAsync(world);
        await _repo.SaveChangesAsync();
        return true;
    }

    private async Task BumpTileAsync(int tileId)
    {
        var tile = await _repo.GetTileByIdAsync(tileId);
        if (tile != null)
        {
            tile.Version++;
        }
    }

    // -------------------------------------------------------------- Streets

    public async Task<StreetRoadDto?> GetStreetRoadAsync(int streetId)
    {
        var names = await _repo.GetStreetNamesAsync(new[] { streetId });
        if (!names.TryGetValue(streetId, out var name))
        {
            return null;
        }
        var edges = await _repo.GetEdgesByStreetAsync(streetId);
        var nodes = await _repo.GetNodesByIdsAsync(edges.SelectMany(e => new[] { e.FromNodeId, e.ToNodeId }));
        return new StreetRoadDto
        {
            StreetId = streetId,
            Name = name,
            EdgeCount = edges.Count,
            TotalLength = edges.Sum(e => e.Length),
            Edges = _mapper.Map<List<RoadEdgeDto>>(edges),
            Nodes = _mapper.Map<List<RoadNodeDto>>(nodes.OrderBy(n => n.Id))
        };
    }

    private static void RequireWorld(string? world)
    {
        if (string.IsNullOrWhiteSpace(world)) throw new ArgumentException("world is required.");
        if (world.Length > MaxWorldLength) throw new ArgumentException($"world is at most {MaxWorldLength} characters.");
    }
}
