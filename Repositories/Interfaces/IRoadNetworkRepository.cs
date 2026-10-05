using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Roads;

namespace knkwebapi_v2.Repositories.Interfaces;

/// <summary>
/// Persistence for the road network (docs/specs/navigation/DESIGN.md §3, plan Phase 1.4). Queries
/// return tracked entities: <see cref="RoadNetworkService"/> mutates them and calls
/// <see cref="SaveChangesAsync"/>; the tile upsert runs inside <see cref="RunInTransactionAsync{T}"/>.
/// </summary>
public interface IRoadNetworkRepository
{
    // Tiles
    Task<RoadTile?> GetTileAsync(string world, int tileX, int tileZ);
    Task<RoadTile?> GetTileByIdAsync(int id);
    Task<List<RoadTile>> GetTilesByIdsAsync(IEnumerable<int> ids);
    Task<List<RoadTile>> ListTilesAsync(string world);
    /// <summary>Adds and saves, so the tile has an id.</summary>
    Task<RoadTile> AddTileAsync(RoadTile tile);
    /// <summary>SELECT ... FOR UPDATE on the tile row inside the current transaction; no-op off MySQL.</summary>
    Task LockTileAsync(int tileId);
    /// <summary>The tile's proposal row (plan §5.7, D5), or null.</summary>
    Task<RoadTileProposal?> GetProposalAsync(int tileId);
    /// <summary>Every proposal row of the world with its tile, ordered by tile.</summary>
    Task<List<RoadTileProposal>> ListProposalsAsync(string world);

    // Nodes
    Task<RoadNode?> GetNodeAsync(int id);
    Task<List<RoadNode>> GetNodesByIdsAsync(IEnumerable<int> ids);
    Task<List<RoadNode>> GetTileNodesAsync(int tileId);
    Task<List<RoadNode>> GetWorldNodesAsync(string world);
    /// <summary>Nodes whose x/z lie in the inclusive box.</summary>
    Task<List<RoadNode>> GetNodesInBoxAsync(string world, int minX, int minZ, int maxX, int maxZ);
    Task<RoadNode?> GetNodeAtAsync(string world, int x, int y, int z);

    // Edges
    Task<RoadEdge?> GetEdgeAsync(int id);
    Task<List<RoadEdge>> GetTileEdgesAsync(int tileId);
    Task<List<RoadEdge>> GetEdgesTouchingNodesAsync(IEnumerable<int> nodeIds);
    Task<List<RoadEdge>> GetWorldEdgesAsync(string world);
    /// <summary>Edges whose x/z bounding box overlaps the inclusive box.</summary>
    Task<List<RoadEdge>> GetEdgesInBoxAsync(string world, int minX, int minZ, int maxX, int maxZ);
    Task<List<RoadEdge>> GetEdgesByStreetAsync(int streetId);
    Task<List<RoadEdge>> GetEdgesByProfileAsync(int profileId);
    Task<List<RoadSurvey>> GetSurveysByProfileAsync(int profileId);
    Task<PagedResult<RoadEdge>> SearchEdgesAsync(PagedQuery query);
    /// <summary>streetId → (edge count, total length) for the given streets, or for every labelled street when null.</summary>
    Task<Dictionary<int, (int edgeCount, double totalLength)>> GetStreetEdgeStatsAsync(IEnumerable<int>? streetIds);
    Task<List<int>> GetLabelledStreetIdsAsync(string world);

    // Profiles, surveys, seeds
    Task<List<RoadProfile>> ListProfilesAsync();
    Task<RoadProfile?> GetProfileAsync(int id);
    Task<RoadProfile?> GetProfileByNameAsync(string name);
    Task<Dictionary<int, RoadClass>> GetProfileClassesAsync();
    Task<List<RoadSurvey>> ListSurveysAsync(string world);
    Task<List<RoadSeed>> ListSeedsAsync(string world);
    Task<RoadSeed?> GetSeedAsync(int id);
    Task<bool> SurveyExistsAsync(int id);

    // Other entities the road network reads
    /// <summary>Structures whose Location lies in the inclusive x/z box of the world, as labeler input.</summary>
    Task<List<RoadStreetLabeler.Structure>> GetStructuresWithLocationInBoxAsync(string world, int minX, int minZ, int maxX, int maxZ);
    /// <summary>Every domain (Town, District, Structure, GateStructure, plain Domain) whose Location lies in the box (plan D12).</summary>
    Task<List<RoadSeedLocationDto>> GetDomainLocationsInBoxAsync(string world, int minX, int minZ, int maxX, int maxZ);
    Task<Dictionary<int, string>> GetStreetNamesAsync(IEnumerable<int> streetIds);
    Task<bool> StreetExistsAsync(int streetId);
    /// <summary>Which of the ids are Towns.</summary>
    Task<List<int>> GetExistingTownIdsAsync(IEnumerable<int> townIds);

    // Unit of work
    void Add<T>(T entity) where T : class;
    void AddRange<T>(IEnumerable<T> entities) where T : class;
    void Remove<T>(T entity) where T : class;
    void RemoveRange<T>(IEnumerable<T> entities) where T : class;
    Task SaveChangesAsync();
    /// <summary>Runs work in one database transaction (committed on success, rolled back on an
    /// exception); straight through on the in-memory provider (plan R30 pattern).</summary>
    Task<T> RunInTransactionAsync<T>(Func<Task<T>> work);
}
