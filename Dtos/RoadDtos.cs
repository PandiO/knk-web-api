using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

// Road navigation DTOs (docs/specs/navigation/DESIGN.md §3, IMPLEMENTATION_PLAN.md Phase 1.3).
// Every property carries [JsonPropertyName("camelCase")]: the global naming policy is null
// (PascalCase) and the plugin's Java DTOs mirror these names with @JsonProperty (Phase 2e).
// Enums serialize by name (global JsonStringEnumConverter); RoadEdgeFlags is exposed as a string
// array so the bit values never leave the database.
namespace knkwebapi_v2.Dtos
{
    /// <summary>One material of a profile; also the shape of RoadProfile.MaterialsJson (JsonColumn).</summary>
    public class RoadMaterialDto
    {
        [JsonPropertyName("material")]
        public string Material { get; set; } = null!;

        [JsonPropertyName("role")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RoadMaterialRole Role { get; set; } = RoadMaterialRole.Surface;

        /// <summary>True for materials that also build houses/kerbs; limited by the plugin's ambiguity reach (DESIGN §5.1).</summary>
        [JsonPropertyName("ambiguous")]
        public bool Ambiguous { get; set; }

        /// <summary>Share of survey samples in the road centre that were this material (0..1).</summary>
        [JsonPropertyName("centreShare")]
        public double CentreShare { get; set; }

        /// <summary>Share of survey samples at the road sides that were this material (0..1).</summary>
        [JsonPropertyName("edgeShare")]
        public double EdgeShare { get; set; }

        [JsonPropertyName("samples")]
        public int Samples { get; set; }
    }

    public class RoadProfileDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("roadClass")]
        public RoadClass RoadClass { get; set; }

        [JsonPropertyName("costMultiplier")]
        public double CostMultiplier { get; set; }

        [JsonPropertyName("materials")]
        public List<RoadMaterialDto> Materials { get; set; } = new();

        [JsonPropertyName("widthMin")]
        public int WidthMin { get; set; }

        [JsonPropertyName("widthMax")]
        public int WidthMax { get; set; }

        [JsonPropertyName("sampleCount")]
        public int SampleCount { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>Town domain ids the profile is limited to; null = everywhere (plan D8).</summary>
        [JsonPropertyName("scopeTownIds")]
        public List<int>? ScopeTownIds { get; set; }

        /// <summary>Accumulated survey statistics, opaque to the API (plan D5); an empty object until the first survey.</summary>
        [JsonPropertyName("stats")]
        public JsonElement? Stats { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>POST/PUT body of a profile: the plugin's ProfileLearner sends the whole recomputed
    /// profile after a survey; the web app sends admin edits.</summary>
    public class RoadProfileUpsertDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("roadClass")]
        public RoadClass RoadClass { get; set; } = RoadClass.Road;

        [JsonPropertyName("costMultiplier")]
        public double CostMultiplier { get; set; } = 1.0;

        [JsonPropertyName("materials")]
        public List<RoadMaterialDto> Materials { get; set; } = new();

        [JsonPropertyName("widthMin")]
        public int WidthMin { get; set; } = 1;

        [JsonPropertyName("widthMax")]
        public int WidthMax { get; set; } = 7;

        [JsonPropertyName("sampleCount")]
        public int SampleCount { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("scopeTownIds")]
        public List<int>? ScopeTownIds { get; set; }

        /// <summary>Null keeps the stored statistics (an admin edit); an object replaces them (a survey merge).</summary>
        [JsonPropertyName("stats")]
        public JsonElement? Stats { get; set; }
    }

    public class RoadBreadcrumbPointDto
    {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }

        [JsonPropertyName("onRoad")]
        public bool OnRoad { get; set; }
    }

    public class RoadSurveyCreateDto
    {
        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("profileId")]
        public int? ProfileId { get; set; }

        [JsonPropertyName("startedAt")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("endedAt")]
        public DateTime? EndedAt { get; set; }

        [JsonPropertyName("sampleCount")]
        public int SampleCount { get; set; }

        [JsonPropertyName("breadcrumb")]
        public List<RoadBreadcrumbPointDto> Breadcrumb { get; set; } = new();

        /// <summary>Material histogram by lateral offset and width histogram; opaque to the API.</summary>
        [JsonPropertyName("stats")]
        public JsonElement? Stats { get; set; }
    }

    public class RoadSurveyDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("profileId")]
        public int? ProfileId { get; set; }

        [JsonPropertyName("startedByUserId")]
        public int? StartedByUserId { get; set; }

        [JsonPropertyName("startedAt")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("endedAt")]
        public DateTime? EndedAt { get; set; }

        [JsonPropertyName("sampleCount")]
        public int SampleCount { get; set; }

        [JsonPropertyName("breadcrumb")]
        public List<RoadBreadcrumbPointDto> Breadcrumb { get; set; } = new();

        [JsonPropertyName("stats")]
        public JsonElement? Stats { get; set; }
    }

    public class RoadTileDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("tileX")]
        public int TileX { get; set; }

        [JsonPropertyName("tileZ")]
        public int TileZ { get; set; }

        /// <summary>The ETag of GET api/road-tiles/{world}/{tileX}/{tileZ}/graph.</summary>
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("builtAt")]
        public DateTime? BuiltAt { get; set; }

        [JsonPropertyName("builderVersion")]
        public int BuilderVersion { get; set; }

        [JsonPropertyName("dirty")]
        public bool Dirty { get; set; }

        /// <summary>Detected: the next build is uploaded directly; Curated: a build makes a proposal
        /// (plan §5.7, D1).</summary>
        [JsonPropertyName("state")]
        public RoadTileState State { get; set; }

        [JsonPropertyName("curatedAt")]
        public DateTime? CuratedAt { get; set; }

        [JsonPropertyName("cellCount")]
        public int CellCount { get; set; }

        [JsonPropertyName("nodeCount")]
        public int NodeCount { get; set; }

        [JsonPropertyName("edgeCount")]
        public int EdgeCount { get; set; }

        [JsonPropertyName("levelCount")]
        public int LevelCount { get; set; }

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();
    }

    public class RoadNodeDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }

        [JsonPropertyName("tileId")]
        public int TileId { get; set; }

        [JsonPropertyName("kind")]
        public RoadNodeKind Kind { get; set; }

        [JsonPropertyName("source")]
        public RoadNodeSource Source { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("componentId")]
        public int ComponentId { get; set; }

        [JsonPropertyName("locked")]
        public bool Locked { get; set; }

        /// <summary>Designed plaza radius when the node is a plaza centre (DESIGN §5.6 step 4).</summary>
        [JsonPropertyName("plazaRadius")]
        public int? PlazaRadius { get; set; }
    }

    public class RoadEdgeDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("fromNodeId")]
        public int FromNodeId { get; set; }

        [JsonPropertyName("toNodeId")]
        public int ToNodeId { get; set; }

        [JsonPropertyName("tileId")]
        public int TileId { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        /// <summary>[[x,y,z],...] from the From node to the To node.</summary>
        [JsonPropertyName("geometry")]
        public int[][] Geometry { get; set; } = Array.Empty<int[]>();

        [JsonPropertyName("length")]
        public double Length { get; set; }

        [JsonPropertyName("minX")]
        public int MinX { get; set; }

        [JsonPropertyName("minY")]
        public int MinY { get; set; }

        [JsonPropertyName("minZ")]
        public int MinZ { get; set; }

        [JsonPropertyName("maxX")]
        public int MaxX { get; set; }

        [JsonPropertyName("maxY")]
        public int MaxY { get; set; }

        [JsonPropertyName("maxZ")]
        public int MaxZ { get; set; }

        [JsonPropertyName("avgWidth")]
        public double AvgWidth { get; set; }

        [JsonPropertyName("profileId")]
        public int? ProfileId { get; set; }

        [JsonPropertyName("streetId")]
        public int? StreetId { get; set; }

        [JsonPropertyName("streetSource")]
        public RoadStreetSource StreetSource { get; set; }

        [JsonPropertyName("costMultiplier")]
        public double CostMultiplier { get; set; }

        /// <summary>Subset of "Oneway", "NoGps", "Closed".</summary>
        [JsonPropertyName("flags")]
        public List<string> Flags { get; set; } = new();

        [JsonPropertyName("gateDoorIds")]
        public List<int> GateDoorIds { get; set; } = new();

        [JsonPropertyName("domainIds")]
        public List<int> DomainIds { get; set; } = new();

        [JsonPropertyName("regionIds")]
        public List<string> RegionIds { get; set; } = new();

        [JsonPropertyName("source")]
        public RoadEdgeSource Source { get; set; }

        [JsonPropertyName("status")]
        public RoadEdgeStatus Status { get; set; }

        /// <summary>An admin kept this edge when a proposal wanted to remove it (plan §5.7, D4).</summary>
        [JsonPropertyName("confirmed")]
        public bool Confirmed { get; set; }
    }

    /// <summary>GET api/road-tiles/{world}/{tileX}/{tileZ}/graph: the tile's own nodes and the edges it
    /// owns. A stitch edge may reference a node of the neighbour tile by id (plan D7).</summary>
    public class RoadTileGraphDto
    {
        [JsonPropertyName("tile")]
        public RoadTileDto Tile { get; set; } = null!;

        [JsonPropertyName("nodes")]
        public List<RoadNodeDto> Nodes { get; set; } = new();

        [JsonPropertyName("edges")]
        public List<RoadEdgeDto> Edges { get; set; } = new();
    }

    public class RoadTileGraphNodeDto
    {
        /// <summary>Client-chosen, unique within the payload; edges reference it.</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; } = null!;

        /// <summary>The matched node of this tile (DESIGN §5.7), or null for a new node.</summary>
        [JsonPropertyName("existingId")]
        public int? ExistingId { get; set; }

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }

        [JsonPropertyName("kind")]
        public RoadNodeKind Kind { get; set; } = RoadNodeKind.Junction;
    }

    public class RoadTileGraphEdgeDto
    {
        /// <summary>The matched detected edge of this tile, or null for a new edge.</summary>
        [JsonPropertyName("existingId")]
        public int? ExistingId { get; set; }

        /// <summary>A node key of this payload, or "id:&lt;n&gt;" for an existing node of another tile.</summary>
        [JsonPropertyName("fromKey")]
        public string FromKey { get; set; } = null!;

        [JsonPropertyName("toKey")]
        public string ToKey { get; set; } = null!;

        [JsonPropertyName("geometry")]
        public int[][] Geometry { get; set; } = Array.Empty<int[]>();

        [JsonPropertyName("length")]
        public double Length { get; set; }

        [JsonPropertyName("avgWidth")]
        public double AvgWidth { get; set; }

        [JsonPropertyName("profileId")]
        public int? ProfileId { get; set; }

        [JsonPropertyName("gateDoorIds")]
        public List<int> GateDoorIds { get; set; } = new();

        [JsonPropertyName("domainIds")]
        public List<int> DomainIds { get; set; } = new();

        [JsonPropertyName("regionIds")]
        public List<string> RegionIds { get; set; } = new();
    }

    /// <summary>PUT api/road-tiles/{world}/{tileX}/{tileZ}/graph: one tile's build result.</summary>
    public class RoadTileGraphUpsertDto
    {
        [JsonPropertyName("builderVersion")]
        public int BuilderVersion { get; set; }

        [JsonPropertyName("cellCount")]
        public int CellCount { get; set; }

        [JsonPropertyName("levelCount")]
        public int LevelCount { get; set; }

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();

        [JsonPropertyName("nodes")]
        public List<RoadTileGraphNodeDto> Nodes { get; set; } = new();

        [JsonPropertyName("edges")]
        public List<RoadTileGraphEdgeDto> Edges { get; set; } = new();
    }

    /// <summary>What the upsert did, for the plugin's build summary (DESIGN §7.1 B.2).</summary>
    public class RoadTileUpsertResultDto
    {
        [JsonPropertyName("tile")]
        public RoadTileDto Tile { get; set; } = null!;

        [JsonPropertyName("nodesCreated")]
        public int NodesCreated { get; set; }

        [JsonPropertyName("nodesUpdated")]
        public int NodesUpdated { get; set; }

        [JsonPropertyName("nodesDeleted")]
        public int NodesDeleted { get; set; }

        [JsonPropertyName("edgesCreated")]
        public int EdgesCreated { get; set; }

        [JsonPropertyName("edgesUpdated")]
        public int EdgesUpdated { get; set; }

        [JsonPropertyName("edgesDeleted")]
        public int EdgesDeleted { get; set; }

        [JsonPropertyName("stitchEdges")]
        public int StitchEdges { get; set; }

        [JsonPropertyName("labelledEdges")]
        public int LabelledEdges { get; set; }

        [JsonPropertyName("unlabelledEdges")]
        public int UnlabelledEdges { get; set; }

        /// <summary>Street-label conflicts, one line each (also appended to the tile warnings).</summary>
        [JsonPropertyName("conflicts")]
        public List<string> Conflicts { get; set; } = new();

        /// <summary>Detected nodes of this tile the build no longer produced (DESIGN §5.7).</summary>
        [JsonPropertyName("deletedNodes")]
        public List<RoadNodeDto> DeletedNodes { get; set; } = new();

        /// <summary>Other tiles whose Version changed because a stitch edge they owned was replaced (plan D7).</summary>
        [JsonPropertyName("bumpedTileIds")]
        public List<int> BumpedTileIds { get; set; } = new();
    }

    public class RoadStreetRefDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
    }

    public class RoadComponentDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("nodeCount")]
        public int NodeCount { get; set; }
    }

    /// <summary>GET api/road-network/meta?world=: what the plugin needs besides the tiles.</summary>
    public class RoadNetworkMetaDto
    {
        [JsonPropertyName("profiles")]
        public List<RoadProfileDto> Profiles { get; set; } = new();

        /// <summary>Every street some edge of the world is labelled with.</summary>
        [JsonPropertyName("streets")]
        public List<RoadStreetRefDto> Streets { get; set; } = new();

        [JsonPropertyName("components")]
        public List<RoadComponentDto> Components { get; set; } = new();
    }

    public class RoadSeedDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }

        [JsonPropertyName("source")]
        public RoadSeedSource Source { get; set; }

        [JsonPropertyName("surveyId")]
        public int? SurveyId { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }

    public class RoadSeedCreateDto
    {
        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }

        [JsonPropertyName("source")]
        public RoadSeedSource Source { get; set; } = RoadSeedSource.Admin;

        [JsonPropertyName("surveyId")]
        public int? SurveyId { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }

    /// <summary>PUT api/road-nodes/{id}: null fields are left as they are. Editing a node locks it
    /// unless <c>locked</c> says otherwise (DESIGN §3.5).</summary>
    public class RoadNodeUpdateDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>True clears the name (null leaves it).</summary>
        [JsonPropertyName("clearName")]
        public bool ClearName { get; set; }

        [JsonPropertyName("kind")]
        public RoadNodeKind? Kind { get; set; }

        [JsonPropertyName("locked")]
        public bool? Locked { get; set; }

        /// <summary>Move the node (rev. 5): all three or none; within its own tile, onto a free
        /// position. The ends of its edges follow.</summary>
        [JsonPropertyName("x")]
        public int? X { get; set; }

        [JsonPropertyName("y")]
        public int? Y { get; set; }

        [JsonPropertyName("z")]
        public int? Z { get; set; }

        /// <summary>Make the node the centre of a designed plaza of this radius (1-32); Junction
        /// and Anchor nodes only (DESIGN §5.6 step 4).</summary>
        [JsonPropertyName("plazaRadius")]
        public int? PlazaRadius { get; set; }

        /// <summary>True clears the plaza (null radius leaves it).</summary>
        [JsonPropertyName("clearPlaza")]
        public bool ClearPlaza { get; set; }
    }

    /// <summary>POST api/road-nodes/anchor: a manual split point (DESIGN §5.6).</summary>
    public class RoadNodeAnchorDto
    {
        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    /// <summary>POST api/road-nodes/merge: <c>mergeNodeId</c>'s edges move to <c>keepNodeId</c> and the
    /// merged node is deleted.</summary>
    public class RoadNodeMergeDto
    {
        [JsonPropertyName("keepNodeId")]
        public int KeepNodeId { get; set; }

        [JsonPropertyName("mergeNodeId")]
        public int MergeNodeId { get; set; }
    }

    /// <summary>PUT api/road-edges/{id}: null fields are left as they are.</summary>
    public class RoadEdgeUpdateDto
    {
        /// <summary>Sets the street (StreetSource becomes Manual).</summary>
        [JsonPropertyName("streetId")]
        public int? StreetId { get; set; }

        /// <summary>True removes the street label (StreetSource becomes None).</summary>
        [JsonPropertyName("clearStreet")]
        public bool ClearStreet { get; set; }

        /// <summary>With a street change: also label the edges that continue this one along the road
        /// (DESIGN §5.11 rule 3), stopping at edges that carry a different Manual street.</summary>
        [JsonPropertyName("propagate")]
        public bool Propagate { get; set; }

        /// <summary>Class override: the profile whose road class the edge should use.</summary>
        [JsonPropertyName("profileId")]
        public int? ProfileId { get; set; }

        [JsonPropertyName("clearProfile")]
        public bool ClearProfile { get; set; }

        [JsonPropertyName("costMultiplier")]
        public double? CostMultiplier { get; set; }

        /// <summary>Replaces the flag set: subset of "Oneway", "NoGps", "Closed"; [] clears them.</summary>
        [JsonPropertyName("flags")]
        public List<string>? Flags { get; set; }

        /// <summary>True keeps a detected edge that a proposal wanted to remove (plan §5.7, D4) and
        /// locks both its nodes; false takes that back. Only for Detected edges.</summary>
        [JsonPropertyName("confirmed")]
        public bool? Confirmed { get; set; }
    }

    /// <summary>PUT api/road-tiles/{world}/{tileX}/{tileZ}/state (plan §5.7, D1).</summary>
    public class RoadTileStateDto
    {
        [JsonPropertyName("state")]
        public RoadTileState State { get; set; }
    }

    /// <summary>
    /// PUT api/road-tiles/{world}/{tileX}/{tileZ}/proposal (plan §5.7, D5): the plugin's proposal for
    /// a Curated tile. <c>items</c> and <c>rejected</c> are JSON arrays in the plugin's item format;
    /// the counts are what listings show.
    /// </summary>
    public class RoadTileProposalUpsertDto
    {
        [JsonPropertyName("baseVersion")]
        public int BaseVersion { get; set; }

        [JsonPropertyName("builderVersion")]
        public int BuilderVersion { get; set; }

        [JsonPropertyName("createdBy")]
        public string? CreatedBy { get; set; }

        [JsonPropertyName("cellCount")]
        public int CellCount { get; set; }

        [JsonPropertyName("levelCount")]
        public int LevelCount { get; set; }

        [JsonPropertyName("warnings")]
        public List<string>? Warnings { get; set; }

        [JsonPropertyName("items")]
        public JsonElement Items { get; set; }

        [JsonPropertyName("rejected")]
        public JsonElement Rejected { get; set; }

        [JsonPropertyName("addedCount")]
        public int AddedCount { get; set; }

        [JsonPropertyName("removedCount")]
        public int RemovedCount { get; set; }

        [JsonPropertyName("changedCount")]
        public int ChangedCount { get; set; }

        [JsonPropertyName("movedCount")]
        public int MovedCount { get; set; }
    }

    /// <summary>A tile's proposal without its items (GET api/road-tiles/proposals?world=).</summary>
    public class RoadTileProposalSummaryDto
    {
        [JsonPropertyName("tileId")]
        public int TileId { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("tileX")]
        public int TileX { get; set; }

        [JsonPropertyName("tileZ")]
        public int TileZ { get; set; }

        [JsonPropertyName("baseVersion")]
        public int BaseVersion { get; set; }

        /// <summary>The tile's current Version: a different one means the graph changed since.</summary>
        [JsonPropertyName("tileVersion")]
        public int TileVersion { get; set; }

        [JsonPropertyName("builderVersion")]
        public int BuilderVersion { get; set; }

        [JsonPropertyName("createdBy")]
        public string? CreatedBy { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("addedCount")]
        public int AddedCount { get; set; }

        [JsonPropertyName("removedCount")]
        public int RemovedCount { get; set; }

        [JsonPropertyName("changedCount")]
        public int ChangedCount { get; set; }

        [JsonPropertyName("movedCount")]
        public int MovedCount { get; set; }

        [JsonPropertyName("rejectedCount")]
        public int RejectedCount { get; set; }
    }

    /// <summary>GET api/road-tiles/{world}/{tileX}/{tileZ}/proposal: the summary plus the items and the
    /// rejected list.</summary>
    public class RoadTileProposalDto : RoadTileProposalSummaryDto
    {
        [JsonPropertyName("cellCount")]
        public int CellCount { get; set; }

        [JsonPropertyName("levelCount")]
        public int LevelCount { get; set; }

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();

        [JsonPropertyName("items")]
        public JsonElement Items { get; set; } = EmptyArray();

        [JsonPropertyName("rejected")]
        public JsonElement Rejected { get; set; } = EmptyArray();

        /// <summary>A fresh "[]" (a default JsonElement cannot be serialized).</summary>
        public static JsonElement EmptyArray()
        {
            using var document = JsonDocument.Parse("[]");
            return document.RootElement.Clone();
        }
    }

    public class RoadEdgeUpdateResultDto
    {
        [JsonPropertyName("edge")]
        public RoadEdgeDto Edge { get; set; } = null!;

        /// <summary>Every edge the call changed, the edge itself first, then the propagated ones.</summary>
        [JsonPropertyName("changedEdgeIds")]
        public List<int> ChangedEdgeIds { get; set; } = new();
    }

    /// <summary>POST api/road-edges/prune: detected edges to remove for good (one transaction).</summary>
    public class RoadEdgePruneDto
    {
        [JsonPropertyName("edgeIds")]
        public List<int> EdgeIds { get; set; } = new();
    }

    public class RoadEdgePruneResultDto
    {
        /// <summary>One PrunedEdge tombstone per pruned edge, in request order (unprune with DELETE api/road-nodes/{id}/prune).</summary>
        [JsonPropertyName("tombstones")]
        public List<RoadNodeDto> Tombstones { get; set; } = new();

        /// <summary>Detected junctions and endpoints left without any edge, deleted with them.</summary>
        [JsonPropertyName("deletedNodeIds")]
        public List<int> DeletedNodeIds { get; set; } = new();
    }

    /// <summary>POST api/road-edges: a recorded edge (DESIGN §5.10). Both ends snap to the nearest node
    /// within 3 blocks, else an Anchor is created there.</summary>
    public class RoadEdgeRecordDto
    {
        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("geometry")]
        public int[][] Geometry { get; set; } = Array.Empty<int[]>();

        /// <summary>Walked length; null = the polyline's length.</summary>
        [JsonPropertyName("length")]
        public double? Length { get; set; }

        [JsonPropertyName("avgWidth")]
        public double AvgWidth { get; set; } = 1.0;

        [JsonPropertyName("profileId")]
        public int? ProfileId { get; set; }

        [JsonPropertyName("streetId")]
        public int? StreetId { get; set; }

        [JsonPropertyName("gateDoorIds")]
        public List<int> GateDoorIds { get; set; } = new();

        [JsonPropertyName("domainIds")]
        public List<int> DomainIds { get; set; } = new();

        [JsonPropertyName("regionIds")]
        public List<string> RegionIds { get; set; } = new();
    }

    /// <summary>GET api/Streets/{id}/road: the street's edges and the nodes they use.</summary>
    public class StreetRoadDto
    {
        [JsonPropertyName("streetId")]
        public int StreetId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("edgeCount")]
        public int EdgeCount { get; set; }

        [JsonPropertyName("totalLength")]
        public double TotalLength { get; set; }

        [JsonPropertyName("edges")]
        public List<RoadEdgeDto> Edges { get; set; } = new();

        [JsonPropertyName("nodes")]
        public List<RoadNodeDto> Nodes { get; set; } = new();
    }

    /// <summary>GET api/road-network/seed-locations: a domain's Location inside the box (plan D12).</summary>
    public class RoadSeedLocationDto
    {
        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        /// <summary>Town, District, Structure, GateStructure or Domain (the CLR type name).</summary>
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }
    }
}
