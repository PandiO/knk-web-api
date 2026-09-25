using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    public interface ISiegeScenarioService
    {
        Task<IEnumerable<SiegeScenarioListDto>> GetAllAsync();
        Task<SiegeScenarioReadDto?> GetByIdAsync(int id);
        Task<SiegeScenarioReadDto> CreateAsync(SiegeScenarioUpsertDto dto);
        Task UpdateAsync(int id, SiegeScenarioUpsertDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<SiegeScenarioListDto>> SearchAsync(PagedQueryDto query);
        Task<SiegeScenarioReadinessDto> GetReadinessAsync(int id);

        Task<List<SiegeTeamReadDto>> GetTeamsAsync(int siegeScenarioId);
        Task<SiegeTeamReadDto?> GetTeamByIdAsync(int id);
        Task<SiegeTeamReadDto> CreateTeamAsync(int siegeScenarioId, SiegeTeamUpsertDto dto);
        Task UpdateTeamAsync(int id, SiegeTeamUpsertDto dto);
        Task DeleteTeamAsync(int id);
        Task<PagedResultDto<SiegeTeamReadDto>> SearchTeamsAsync(PagedQueryDto query);

        Task<List<SiegeSpawnpointReadDto>> GetSpawnpointsAsync(int siegeTeamId);
        Task<SiegeSpawnpointReadDto?> GetSpawnpointByIdAsync(int id);
        Task<SiegeSpawnpointReadDto> CreateSpawnpointAsync(int siegeTeamId, SiegeSpawnpointUpsertDto dto);
        Task UpdateSpawnpointAsync(int id, SiegeSpawnpointUpsertDto dto);
        Task DeleteSpawnpointAsync(int id);

        Task<List<SiegeObjectiveReadDto>> GetObjectivesAsync(int siegeScenarioId);
        Task<SiegeObjectiveReadDto?> GetObjectiveByIdAsync(int id);
        Task<SiegeObjectiveReadDto> CreateObjectiveAsync(int siegeScenarioId, SiegeObjectiveUpsertDto dto);
        Task UpdateObjectiveAsync(int id, SiegeObjectiveUpsertDto dto);
        Task DeleteObjectiveAsync(int id);
    }
}
