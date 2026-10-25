using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class GateDoorRepository : IGateDoorRepository
    {
        private readonly KnKDbContext _context;

        public GateDoorRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<GateDoor>> GetByStructureIdAsync(int gateStructureId)
        {
            return await BuildDoorQuery()
                .Where(d => d.GateStructureId == gateStructureId)
                .ToListAsync();
        }

        public async Task<GateDoor?> GetByIdAsync(int id)
        {
            return await BuildDoorQuery()
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<GateDoor?> GetByIdWithSnapshotsAsync(int id)
        {
            return await BuildDoorQuery()
                .Include(d => d.BlockSnapshots)
                .Include(d => d.OpenedBlockSnapshots)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        // KNG-273: the flat, paged door list behind the web app's GateDoor dashboard and pickers.
        // Filters: gateStructureId, gateType, openedState, isActive. The search term matches the
        // door's name or its gate's name.
        public async Task<PagedResult<GateDoor>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.GateDoors.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(d => d.Name.ToLower().Contains(searchLower) ||
                                                 d.GateStructure.Name.ToLower().Contains(searchLower));
            }

            if (query.Filters != null)
            {
                if (query.Filters.TryGetValue("gateStructureId", out var structureIdStr) && int.TryParse(structureIdStr, out var structureId))
                {
                    queryable = queryable.Where(d => d.GateStructureId == structureId);
                }
                if (query.Filters.TryGetValue("gateType", out var gateType) &&
                    Enum.TryParse<GateType>(gateType, true, out var parsedGateType))
                {
                    queryable = queryable.Where(d => d.GateType == parsedGateType);
                }
                if (query.Filters.TryGetValue("openedState", out var openedState) &&
                    Enum.TryParse<GateDoorOpenState>(openedState, true, out var parsedOpenedState))
                {
                    queryable = queryable.Where(d => d.OpenedState == parsedOpenedState);
                }
                if (query.Filters.TryGetValue("isActive", out var isActiveStr) && bool.TryParse(isActiveStr, out var isActive))
                {
                    queryable = queryable.Where(d => d.IsActive == isActive);
                }
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Include(d => d.GateStructure)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<GateDoor>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private static IQueryable<GateDoor> ApplySorting(IQueryable<GateDoor> queryable, string? sortBy, bool sortDescending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
            {
                return queryable.OrderBy(d => d.GateStructure.Name).ThenBy(d => d.Name);
            }

            return sortBy.ToLower() switch
            {
                "id" => sortDescending ? queryable.OrderByDescending(d => d.Id) : queryable.OrderBy(d => d.Id),
                "name" => sortDescending ? queryable.OrderByDescending(d => d.Name) : queryable.OrderBy(d => d.Name),
                "gatestructureid" => sortDescending ? queryable.OrderByDescending(d => d.GateStructureId) : queryable.OrderBy(d => d.GateStructureId),
                "gatestructurename" => sortDescending ? queryable.OrderByDescending(d => d.GateStructure.Name) : queryable.OrderBy(d => d.GateStructure.Name),
                "gatetype" => sortDescending ? queryable.OrderByDescending(d => d.GateType) : queryable.OrderBy(d => d.GateType),
                "openedstate" => sortDescending ? queryable.OrderByDescending(d => d.OpenedState) : queryable.OrderBy(d => d.OpenedState),
                "isactive" => sortDescending ? queryable.OrderByDescending(d => d.IsActive) : queryable.OrderBy(d => d.IsActive),
                "healthcurrent" => sortDescending ? queryable.OrderByDescending(d => d.HealthCurrent) : queryable.OrderBy(d => d.HealthCurrent),
                _ => queryable.OrderBy(d => d.GateStructure.Name).ThenBy(d => d.Name)
            };
        }

        public async Task AddAsync(GateDoor gateDoor)
        {
            await _context.Set<GateDoor>().AddAsync(gateDoor);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(GateDoor gateDoor)
        {
            _context.Set<GateDoor>().Update(gateDoor);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var gateDoor = await _context.Set<GateDoor>().FindAsync(id);
            if (gateDoor != null)
            {
                _context.Set<GateDoor>().Remove(gateDoor);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<GateDoor>> GetActiveDoorsAsync()
        {
            return await BuildDoorQuery()
                .Where(d => d.IsActive)
                .ToListAsync();
        }

        public async Task<bool> IsDoorNameUniqueAsync(int gateStructureId, string name, int? excludeId = null)
        {
            var query = _context.Set<GateDoor>()
                .Where(d => d.GateStructureId == gateStructureId && d.Name == name);

            if (excludeId.HasValue)
            {
                query = query.Where(d => d.Id != excludeId.Value);
            }

            return !await query.AnyAsync();
        }

        public async Task UpdateHealthAsync(int id, double newHealth)
        {
            var door = await _context.Set<GateDoor>().FindAsync(id);
            if (door != null)
            {
                door.HealthCurrent = newHealth;
                if (newHealth <= 0)
                {
                    door.IsDestroyed = true;
                }
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateStateAsync(int id, GateDoorOpenState openedState, bool isDestroyed)
        {
            var door = await _context.Set<GateDoor>().FindAsync(id);
            if (door != null)
            {
                // Respawning is the destroyed -> not destroyed transition; restore full health.
                bool isRespawning = door.IsDestroyed && !isDestroyed;

                door.OpenedState = openedState;
                door.IsDestroyed = isDestroyed;

                if (isRespawning)
                {
                    door.HealthCurrent = door.HealthMax;
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateOperationalSettingsAsync(int id, bool isActive, bool isInvincible)
        {
            var door = await _context.Set<GateDoor>().FindAsync(id);
            if (door != null)
            {
                door.IsActive = isActive;
                door.IsInvincible = isInvincible;
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateRegionDataAsync(int id, bool isOpenedRegion, string regionData)
        {
            var door = await _context.Set<GateDoor>().FindAsync(id);
            if (door != null)
            {
                if (isOpenedRegion)
                    door.OpenedRegionData = regionData;
                else
                    door.ClosedRegionData = regionData;
                await _context.SaveChangesAsync();
            }
        }

        // Block snapshot operations
        public async Task<IEnumerable<GateBlockSnapshot>> GetBlockSnapshotsByDoorIdAsync(int gateDoorId)
        {
            return await _context.Set<GateBlockSnapshot>()
                .Where(bs => bs.GateDoorId == gateDoorId)
                .OrderBy(bs => bs.SortOrder)
                .ToListAsync();
        }

        public async Task AddBlockSnapshotAsync(GateBlockSnapshot snapshot)
        {
            await _context.Set<GateBlockSnapshot>().AddAsync(snapshot);
            await _context.SaveChangesAsync();
        }

        public async Task AddBlockSnapshotsAsync(IEnumerable<GateBlockSnapshot> snapshots)
        {
            await _context.Set<GateBlockSnapshot>().AddRangeAsync(snapshots);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteBlockSnapshotsByDoorIdAsync(int gateDoorId)
        {
            var snapshots = await _context.Set<GateBlockSnapshot>()
                .Where(bs => bs.GateDoorId == gateDoorId)
                .ToListAsync();

            _context.Set<GateBlockSnapshot>().RemoveRange(snapshots);
            await _context.SaveChangesAsync();
        }

        // Opened-block snapshot operations - mirrors the block snapshot operations above
        // exactly, for the separately-scanned fully-open shape. See ROTATION_GAP_FILL_DESIGN.md.
        public async Task<IEnumerable<GateOpenedBlockSnapshot>> GetOpenedBlockSnapshotsByDoorIdAsync(int gateDoorId)
        {
            return await _context.Set<GateOpenedBlockSnapshot>()
                .Where(bs => bs.GateDoorId == gateDoorId)
                .OrderBy(bs => bs.SortOrder)
                .ToListAsync();
        }

        public async Task AddOpenedBlockSnapshotAsync(GateOpenedBlockSnapshot snapshot)
        {
            await _context.Set<GateOpenedBlockSnapshot>().AddAsync(snapshot);
            await _context.SaveChangesAsync();
        }

        public async Task AddOpenedBlockSnapshotsAsync(IEnumerable<GateOpenedBlockSnapshot> snapshots)
        {
            await _context.Set<GateOpenedBlockSnapshot>().AddRangeAsync(snapshots);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteOpenedBlockSnapshotsByDoorIdAsync(int gateDoorId)
        {
            var snapshots = await _context.Set<GateOpenedBlockSnapshot>()
                .Where(bs => bs.GateDoorId == gateDoorId)
                .ToListAsync();

            _context.Set<GateOpenedBlockSnapshot>().RemoveRange(snapshots);
            await _context.SaveChangesAsync();
        }

        private IQueryable<GateDoor> BuildDoorQuery()
        {
            return _context.Set<GateDoor>()
                .Include(d => d.AnchorPoint)
                .Include(d => d.OpenAnchorPoint)
                .Include(d => d.ReferencePoint1)
                .Include(d => d.ReferencePoint2)
                .Include(d => d.HingeAxis)
                .Include(d => d.LeftDoorSeedBlock)
                .Include(d => d.RightDoorSeedBlock)
                .Include(d => d.InfoDisplayLocation)
                .Include(d => d.FallbackMaterial);
        }
    }
}
