using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface ISiegeScenarioRepository
    {
        Task<IEnumerable<SiegeScenario>> GetAllAsync();
        // Full graph: town, hub, districts, gates, teams (clan, banner, spawnpoints + locations),
        // objectives (location, gate + its location).
        Task<SiegeScenario?> GetByIdAsync(int id);
        Task<List<SiegeScenario>> GetByIdsAsync(IEnumerable<int> ids);
        Task AddAsync(SiegeScenario entity);
        Task UpdateAsync(SiegeScenario entity);
        Task DeleteAsync(SiegeScenario entity);
        Task<PagedResult<SiegeScenario>> SearchAsync(PagedQuery query);
        Task<bool> HasMatchHistoryAsync(int siegeScenarioId);

        // Lookups for save-time validation
        Task<Town?> GetTownAsync(int townId);
        Task<List<District>> GetDistrictsAsync(IEnumerable<int> districtIds);
        Task<List<GateStructure>> GetGateStructuresAsync(IEnumerable<int> gateStructureIds);
        // Siege Phase 7: every gate structure in these districts (none given: in any district of the town).
        Task<List<int>> GetAreaGateStructureIdsAsync(int townId, IEnumerable<int> districtIds);
        Task<bool> LocationExistsAsync(int locationId);
        Task<bool> TitleBracketExistsAsync(int titleBracketId);
        Task<Clan?> GetClanAsync(int clanId);
        Task<bool> BannerDesignExistsAsync(int bannerDesignId);

        // Owned teams
        Task<SiegeTeam?> GetTeamByIdAsync(int id);
        Task<List<SiegeTeam>> GetTeamsAsync(int siegeScenarioId);
        Task AddTeamAsync(SiegeTeam team);
        Task UpdateTeamAsync(SiegeTeam team);
        Task DeleteTeamAsync(SiegeTeam team);
        Task<PagedResult<SiegeTeam>> SearchTeamsAsync(PagedQuery query);

        // Owned spawnpoints (of a team)
        Task<SiegeSpawnpoint?> GetSpawnpointByIdAsync(int id);
        Task<List<SiegeSpawnpoint>> GetSpawnpointsAsync(int siegeTeamId);
        Task AddSpawnpointAsync(SiegeSpawnpoint spawnpoint);
        Task UpdateSpawnpointAsync(SiegeSpawnpoint spawnpoint);
        Task DeleteSpawnpointAsync(SiegeSpawnpoint spawnpoint);

        // Owned objectives
        Task<SiegeObjective?> GetObjectiveByIdAsync(int id);
        Task<List<SiegeObjective>> GetObjectivesAsync(int siegeScenarioId);
        Task AddObjectiveAsync(SiegeObjective objective);
        Task UpdateObjectiveAsync(SiegeObjective objective);
        Task DeleteObjectiveAsync(SiegeObjective objective);
    }
}
