using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

/// <summary>
/// The server side of road navigation (docs/specs/navigation/DESIGN.md §3, plan Phase 1.4): tile
/// graph upserts with id matching, stitching and street labelling; profiles, surveys, seeds; the
/// admin review actions on nodes and edges; the per-world metadata the plugin downloads.
/// Validation → ArgumentException, not found → KeyNotFoundException, conflicts →
/// InvalidOperationException (controllers map them to 400/404/409).
/// </summary>
public interface IRoadNetworkService
{
    // Tiles
    Task<List<RoadTileDto>> ListTilesAsync(string world);
    Task<RoadTileGraphDto?> GetTileGraphAsync(string world, int tileX, int tileZ);
    Task<RoadTileUpsertResultDto> UpsertTileGraphAsync(string world, int tileX, int tileZ, RoadTileGraphUpsertDto dto);
    Task<RoadTileDto> MarkTileDirtyAsync(string world, int tileX, int tileZ);

    // Network
    Task<RoadNetworkMetaDto> GetMetaAsync(string world);
    Task<List<RoadSeedLocationDto>> GetSeedLocationsAsync(string world, int minX, int minZ, int maxX, int maxZ);

    // Profiles
    Task<List<RoadProfileDto>> ListProfilesAsync();
    Task<RoadProfileDto?> GetProfileAsync(int id);
    Task<RoadProfileDto> CreateProfileAsync(RoadProfileUpsertDto dto);
    Task<RoadProfileDto> UpdateProfileAsync(int id, RoadProfileUpsertDto dto);
    Task<bool> DeleteProfileAsync(int id);

    // Surveys
    Task<RoadSurveyDto> CreateSurveyAsync(RoadSurveyCreateDto dto, int? startedByUserId);
    Task<List<RoadSurveyDto>> ListSurveysAsync(string world);

    // Seeds
    Task<List<RoadSeedDto>> ListSeedsAsync(string world);
    Task<RoadSeedDto> CreateSeedAsync(RoadSeedCreateDto dto);
    Task<bool> DeleteSeedAsync(int id);

    // Nodes
    Task<RoadNodeDto> UpdateNodeAsync(int id, RoadNodeUpdateDto dto);
    Task<RoadNodeDto> CreateAnchorAsync(RoadNodeAnchorDto dto);
    Task<RoadNodeDto> MergeNodesAsync(RoadNodeMergeDto dto);
    Task<RoadNodeDto> PruneNodeAsync(int id);
    Task<bool> UnpruneNodeAsync(int id);

    // Edges
    Task<PagedResultDto<RoadEdgeDto>> SearchEdgesAsync(PagedQueryDto query);
    Task<RoadEdgeDto> CreateRecordedEdgeAsync(RoadEdgeRecordDto dto);
    Task<RoadEdgeUpdateResultDto> UpdateEdgeAsync(int id, RoadEdgeUpdateDto dto);
    Task<bool> DeleteEdgeAsync(int id);
    Task<RoadEdgePruneResultDto> PruneEdgesAsync(RoadEdgePruneDto dto);

    // Streets
    Task<StreetRoadDto?> GetStreetRoadAsync(int streetId);
}
