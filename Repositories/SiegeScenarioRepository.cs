using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Siege Phase 2 (docs/specs/siege-minigame/DESIGN.md §3.3–3.7): the scenario aggregate - the
    // scenario, its M2M joins and its owned teams/spawnpoints/objectives.
    public class SiegeScenarioRepository : ISiegeScenarioRepository
    {
        private readonly KnKDbContext _context;

        public SiegeScenarioRepository(KnKDbContext context)
        {
            _context = context;
        }

        // The whole scenario graph readiness, runtime-config and the read DTO need. Split query: the
        // collections would otherwise multiply into one very wide join.
        private IQueryable<SiegeScenario> WithGraph() => _context.SiegeScenarios
            .Include(s => s.Town)
            .Include(s => s.HubLocation)
            .Include(s => s.MinTitleBracket)
            .Include(s => s.Districts).ThenInclude(d => d.District)
            .Include(s => s.Gates).ThenInclude(g => g.GateStructure).ThenInclude(g => g.District)
            .Include(s => s.Teams).ThenInclude(t => t.Clan).ThenInclude(c => c!.BannerDesign).ThenInclude(b => b.Layers)
            .Include(s => s.Teams).ThenInclude(t => t.BannerDesign).ThenInclude(b => b!.Layers)
            .Include(s => s.Teams).ThenInclude(t => t.Spawnpoints).ThenInclude(p => p.Location)
            .Include(s => s.Objectives).ThenInclude(o => o.Location)
            .Include(s => s.Objectives).ThenInclude(o => o.GateStructure).ThenInclude(g => g!.Location)
            .AsSplitQuery();

        public async Task<IEnumerable<SiegeScenario>> GetAllAsync()
        {
            return await _context.SiegeScenarios
                .Include(s => s.Town)
                .Include(s => s.Teams)
                .Include(s => s.Objectives)
                .Include(s => s.Gates)
                .AsSplitQuery()
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        public async Task<SiegeScenario?> GetByIdAsync(int id)
        {
            return await WithGraph().FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<List<SiegeScenario>> GetByIdsAsync(IEnumerable<int> ids)
        {
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0) return new List<SiegeScenario>();
            return await WithGraph().Where(s => idList.Contains(s.Id)).ToListAsync();
        }

        public async Task AddAsync(SiegeScenario entity)
        {
            await _context.SiegeScenarios.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(SiegeScenario entity)
        {
            // Loaded entities are already tracked; Update() would mark the whole loaded graph
            // (incl. shared town/gate/clan rows) Modified.
            if (_context.Entry(entity).State == EntityState.Detached) _context.SiegeScenarios.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(SiegeScenario entity)
        {
            // Teams/spawnpoints/objectives and the district/gate/rotation join rows cascade;
            // Town/Location/GateStructure/Clan rows are only referenced, never deleted.
            _context.SiegeScenarios.Remove(entity);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<SiegeScenario>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.SiegeScenarios.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(s => s.Name.ToLower().Contains(searchLower));
            }

            if (TryGetIntFilter(query, "townId", out var townId))
                queryable = queryable.Where(s => s.TownId == townId);

            var totalCount = await queryable.CountAsync();

            queryable = query.SortBy switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(s => s.Name) : queryable.OrderBy(s => s.Name),
                _ => query.SortDescending ? queryable.OrderByDescending(s => s.Id) : queryable.OrderBy(s => s.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(s => s.Town)
                .Include(s => s.Teams)
                .Include(s => s.Objectives)
                .Include(s => s.Gates)
                .AsSplitQuery()
                .ToListAsync();

            return new PagedResult<SiegeScenario>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<bool> HasMatchHistoryAsync(int siegeScenarioId)
        {
            return await _context.SiegeMatches.AnyAsync(m => m.SiegeScenarioId == siegeScenarioId);
        }

        // ---- Lookups ----

        public async Task<Town?> GetTownAsync(int townId)
        {
            return await _context.Towns.FirstOrDefaultAsync(t => t.Id == townId);
        }

        public async Task<List<District>> GetDistrictsAsync(IEnumerable<int> districtIds)
        {
            var ids = districtIds.Distinct().ToList();
            return await _context.Districts.Where(d => ids.Contains(d.Id)).ToListAsync();
        }

        public async Task<List<GateStructure>> GetGateStructuresAsync(IEnumerable<int> gateStructureIds)
        {
            var ids = gateStructureIds.Distinct().ToList();
            return await _context.GateStructures
                .Include(g => g.District)
                .Include(g => g.Location)
                .Where(g => ids.Contains(g.Id))
                .ToListAsync();
        }

        public async Task<List<int>> GetAreaGateStructureIdsAsync(int townId, IEnumerable<int> districtIds)
        {
            var ids = districtIds.Distinct().ToList();
            var query = _context.GateStructures.AsQueryable();
            query = ids.Count > 0
                ? query.Where(g => ids.Contains(g.DistrictId))
                : query.Where(g => g.District.TownId == townId);
            return await query.OrderBy(g => g.Id).Select(g => g.Id).ToListAsync();
        }

        public async Task<bool> LocationExistsAsync(int locationId)
        {
            return await _context.Locations.AnyAsync(l => l.Id == locationId);
        }

        public async Task<bool> TitleBracketExistsAsync(int titleBracketId)
        {
            return await _context.TitleBrackets.AnyAsync(t => t.Id == titleBracketId);
        }

        public async Task<Clan?> GetClanAsync(int clanId)
        {
            return await _context.Clans.FirstOrDefaultAsync(c => c.Id == clanId);
        }

        public async Task<bool> BannerDesignExistsAsync(int bannerDesignId)
        {
            return await _context.BannerDesigns.AnyAsync(b => b.Id == bannerDesignId);
        }

        // ---- Teams ----

        private IQueryable<SiegeTeam> TeamsWithGraph() => _context.SiegeTeams
            .Include(t => t.Clan)
            .Include(t => t.Spawnpoints).ThenInclude(p => p.Location)
            .AsSplitQuery();

        public async Task<SiegeTeam?> GetTeamByIdAsync(int id)
        {
            return await TeamsWithGraph().FirstOrDefaultAsync(t => t.Id == id);
        }

        public async Task<List<SiegeTeam>> GetTeamsAsync(int siegeScenarioId)
        {
            return await TeamsWithGraph()
                .Where(t => t.SiegeScenarioId == siegeScenarioId)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
                .ToListAsync();
        }

        public async Task AddTeamAsync(SiegeTeam team)
        {
            await _context.SiegeTeams.AddAsync(team);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateTeamAsync(SiegeTeam team)
        {
            if (_context.Entry(team).State == EntityState.Detached) _context.SiegeTeams.Update(team);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteTeamAsync(SiegeTeam team)
        {
            // Spawnpoints cascade; objectives/gates holding this team as initial holder/owner are
            // SetNull (back to the "first Defender team" default). The DB's ON DELETE SET NULL does
            // that anyway; loading the few sibling rows lets EF null them itself, so the behaviour
            // doesn't depend on the provider. (Match history rows are left to the DB.)
            await _context.SiegeObjectives.Where(o => o.InitialHolderTeamId == team.Id).LoadAsync();
            await _context.SiegeScenarioGates.Where(g => g.InitialOwnerTeamId == team.Id).LoadAsync();
            _context.SiegeTeams.Remove(team);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<SiegeTeam>> SearchTeamsAsync(PagedQuery query)
        {
            var queryable = _context.SiegeTeams.Include(t => t.Clan).AsQueryable();

            // The web-app picker for InitialHolderTeamId/InitialOwnerTeamId scopes by scenario.
            if (TryGetIntFilter(query, "siegeScenarioId", out var scenarioId))
                queryable = queryable.Where(t => t.SiegeScenarioId == scenarioId);

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(t =>
                    (t.Name != null && t.Name.ToLower().Contains(searchLower)) ||
                    (t.Clan != null && t.Clan.Name.ToLower().Contains(searchLower)));
            }

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .OrderBy(t => t.SiegeScenarioId).ThenBy(t => t.SortOrder).ThenBy(t => t.Id)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<SiegeTeam>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        // ---- Spawnpoints ----

        public async Task<SiegeSpawnpoint?> GetSpawnpointByIdAsync(int id)
        {
            return await _context.SiegeSpawnpoints.Include(p => p.Location).FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<List<SiegeSpawnpoint>> GetSpawnpointsAsync(int siegeTeamId)
        {
            return await _context.SiegeSpawnpoints
                .Include(p => p.Location)
                .Where(p => p.SiegeTeamId == siegeTeamId)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
                .ToListAsync();
        }

        public async Task AddSpawnpointAsync(SiegeSpawnpoint spawnpoint)
        {
            await _context.SiegeSpawnpoints.AddAsync(spawnpoint);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateSpawnpointAsync(SiegeSpawnpoint spawnpoint)
        {
            if (_context.Entry(spawnpoint).State == EntityState.Detached) _context.SiegeSpawnpoints.Update(spawnpoint);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteSpawnpointAsync(SiegeSpawnpoint spawnpoint)
        {
            _context.SiegeSpawnpoints.Remove(spawnpoint);
            await _context.SaveChangesAsync();
        }

        // ---- Objectives ----

        public async Task<SiegeObjective?> GetObjectiveByIdAsync(int id)
        {
            return await _context.SiegeObjectives
                .Include(o => o.Location)
                .Include(o => o.GateStructure)
                .FirstOrDefaultAsync(o => o.Id == id);
        }

        public async Task<List<SiegeObjective>> GetObjectivesAsync(int siegeScenarioId)
        {
            return await _context.SiegeObjectives
                .Include(o => o.Location)
                .Include(o => o.GateStructure)
                .Where(o => o.SiegeScenarioId == siegeScenarioId)
                .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
                .ToListAsync();
        }

        public async Task AddObjectiveAsync(SiegeObjective objective)
        {
            await _context.SiegeObjectives.AddAsync(objective);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateObjectiveAsync(SiegeObjective objective)
        {
            if (_context.Entry(objective).State == EntityState.Detached) _context.SiegeObjectives.Update(objective);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteObjectiveAsync(SiegeObjective objective)
        {
            _context.SiegeObjectives.Remove(objective);
            await _context.SaveChangesAsync();
        }

        private static bool TryGetIntFilter(PagedQuery query, string key, out int value)
        {
            value = 0;
            if (query.Filters == null) return false;
            var match = query.Filters.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            return match.Key != null && int.TryParse(match.Value, out value) && value > 0;
        }
    }
}
