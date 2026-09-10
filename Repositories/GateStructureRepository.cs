using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class GateStructureRepository : IGateStructureRepository
    {
        private readonly KnKDbContext _context;

        public GateStructureRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<GateStructure>> GetAllAsync()
        {
            return await BuildGateQuery()
                .ToListAsync();
        }

        public async Task<GateStructure?> GetByIdAsync(int id)
        {
            return await BuildGateQuery()
                .FirstOrDefaultAsync(gs => gs.Id == id);
        }

        public async Task<GateStructure?> GetByIdWithSnapshotsAsync(int id)
        {
            return await BuildGateQuery()
                .Include(gs => gs.BlockSnapshots)
                .Include(gs => gs.OpenedBlockSnapshots)
                .FirstOrDefaultAsync(gs => gs.Id == id);
        }

        public async Task AddGateStructureAsync(GateStructure gateStructure)
        {
            await _context.Set<GateStructure>().AddAsync(gateStructure);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateGateStructureAsync(GateStructure gateStructure)
        {
            _context.Set<GateStructure>().Update(gateStructure);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteGateStructureAsync(int id)
        {
            var gateStructure = await _context.Set<GateStructure>().FindAsync(id);
            if (gateStructure != null)
            {
                _context.Set<GateStructure>().Remove(gateStructure);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<GateStructure>> GetGatesByDomainAsync(int domainId)
        {
            // Domain is inherited through Structure, need to query via LocationId
            // For now, return all gates (will be refined when Domain relationship is clearer)
            return await BuildGateQuery()
                .ToListAsync();
        }

        public async Task<IEnumerable<GateStructure>> GetActiveGatesAsync()
        {
            return await BuildGateQuery()
                .Where(gs => gs.IsActive)
                .ToListAsync();
        }

        public async Task<bool> IsGateNameUniqueAsync(string name, int domainId, int? excludeId = null)
        {
            var query = _context.Set<GateStructure>()
                .Where(gs => gs.Name == name);

            if (excludeId.HasValue)
            {
                query = query.Where(gs => gs.Id != excludeId.Value);
            }

            return !await query.AnyAsync();
        }

        public async Task<GateStructure?> FindGateByRegionAsync(string regionId)
        {
            return await BuildGateQuery()
                .FirstOrDefaultAsync(gs => 
                    gs.RegionClosedId == regionId || 
                    gs.RegionOpenedId == regionId);
        }

        public async Task UpdateGateHealthAsync(int id, double newHealth)
        {
            var gate = await _context.Set<GateStructure>().FindAsync(id);
            if (gate != null)
            {
                gate.HealthCurrent = newHealth;
                if (newHealth <= 0)
                {
                    gate.IsDestroyed = true;
                }
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateGateStateAsync(int id, bool isOpened, bool isDestroyed, bool isJammed)
        {
            var gate = await _context.Set<GateStructure>().FindAsync(id);
            if (gate != null)
            {
                // Respawning is the destroyed -> not destroyed transition; restore full health.
                bool isRespawning = gate.IsDestroyed && !isDestroyed;

                gate.IsOpened = isOpened;
                gate.IsDestroyed = isDestroyed;
                gate.IsJammed = isJammed;

                if (isRespawning)
                {
                    gate.HealthCurrent = gate.HealthMax;
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateGateOperationalSettingsAsync(int id, bool isActive, bool isInvincible)
        {
            var gate = await _context.Set<GateStructure>().FindAsync(id);
            if (gate != null)
            {
                gate.IsActive = isActive;
                gate.IsInvincible = isInvincible;
                await _context.SaveChangesAsync();
            }
        }

        // Block snapshot operations
        public async Task<IEnumerable<GateBlockSnapshot>> GetBlockSnapshotsByGateIdAsync(int gateId)
        {
            return await _context.Set<GateBlockSnapshot>()
                .Where(bs => bs.GateStructureId == gateId)
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

        public async Task DeleteBlockSnapshotsByGateIdAsync(int gateId)
        {
            var snapshots = await _context.Set<GateBlockSnapshot>()
                .Where(bs => bs.GateStructureId == gateId)
                .ToListAsync();

            _context.Set<GateBlockSnapshot>().RemoveRange(snapshots);
            await _context.SaveChangesAsync();
        }

        // Opened-block snapshot operations - mirrors the block snapshot operations above
        // exactly, for the separately-scanned fully-open shape. See ROTATION_GAP_FILL_DESIGN.md.
        public async Task<IEnumerable<GateOpenedBlockSnapshot>> GetOpenedBlockSnapshotsByGateIdAsync(int gateId)
        {
            return await _context.Set<GateOpenedBlockSnapshot>()
                .Where(bs => bs.GateStructureId == gateId)
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

        public async Task DeleteOpenedBlockSnapshotsByGateIdAsync(int gateId)
        {
            var snapshots = await _context.Set<GateOpenedBlockSnapshot>()
                .Where(bs => bs.GateStructureId == gateId)
                .ToListAsync();

            _context.Set<GateOpenedBlockSnapshot>().RemoveRange(snapshots);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<GateStructure>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Set<GateStructure>().AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(gs => gs.Name.ToLower().Contains(searchLower) || 
                                                   gs.Description.ToLower().Contains(searchLower));
            }

            if (query.Filters != null)
            {
                if (query.Filters.TryGetValue("streetId", out var streetIdStr) && int.TryParse(streetIdStr, out var streetId))
                {
                    queryable = queryable.Where(gs => gs.StreetId == streetId);
                }
                if (query.Filters.TryGetValue("districtId", out var districtIdStr) && int.TryParse(districtIdStr, out var districtId))
                {
                    queryable = queryable.Where(gs => gs.DistrictId == districtId);
                }
                if (query.Filters.TryGetValue("isActive", out var isActiveStr) && bool.TryParse(isActiveStr, out var isActive))
                {
                    queryable = queryable.Where(gs => gs.IsActive == isActive);
                }
                if (query.Filters.TryGetValue("gateType", out var gateType) &&
                    System.Enum.TryParse<GateType>(gateType, true, out var parsedGateType))
                {
                    queryable = queryable.Where(gs => gs.GateType == parsedGateType);
                }
                if (query.Filters.TryGetValue("isOpened", out var isOpenedStr) && bool.TryParse(isOpenedStr, out var isOpened))
                {
                    queryable = queryable.Where(gs => gs.IsOpened == isOpened);
                }
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Include(gs => gs.Location)
                .Include(gs => gs.Street)
                .Include(gs => gs.District)
                .Include(gs => gs.IconMaterial)
                .Include(gs => gs.FallbackMaterial)
                .Include(gs => gs.AnchorPoint)
                .Include(gs => gs.OpenAnchorPoint)
                .Include(gs => gs.ReferencePoint1)
                .Include(gs => gs.ReferencePoint2)
                .Include(gs => gs.HingeAxis)
                .Include(gs => gs.LeftDoorSeedBlock)
                .Include(gs => gs.RightDoorSeedBlock)
                .Include(gs => gs.InfoDisplayLocation)
                .Include(gs => gs.GuardSpawnLocations)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<GateStructure>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private IQueryable<GateStructure> ApplySorting(IQueryable<GateStructure> queryable, string? sortBy, bool sortDescending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return queryable.OrderBy(gs => gs.Name);

            return sortBy.ToLower() switch
            {
                "name" => sortDescending ? queryable.OrderByDescending(gs => gs.Name) : queryable.OrderBy(gs => gs.Name),
                "id" => sortDescending ? queryable.OrderByDescending(gs => gs.Id) : queryable.OrderBy(gs => gs.Id),
                "housenumber" => sortDescending ? queryable.OrderByDescending(gs => gs.HouseNumber) : queryable.OrderBy(gs => gs.HouseNumber),
                "createdat" => sortDescending ? queryable.OrderByDescending(gs => gs.CreatedAt) : queryable.OrderBy(gs => gs.CreatedAt),
                "isactive" => sortDescending ? queryable.OrderByDescending(gs => gs.IsActive) : queryable.OrderBy(gs => gs.IsActive),
                "gatetype" => sortDescending ? queryable.OrderByDescending(gs => gs.GateType) : queryable.OrderBy(gs => gs.GateType),
                "healthcurrent" => sortDescending ? queryable.OrderByDescending(gs => gs.HealthCurrent) : queryable.OrderBy(gs => gs.HealthCurrent),
                _ => queryable.OrderBy(gs => gs.Name)
            };
        }

        private IQueryable<GateStructure> BuildGateQuery()
        {
            return _context.Set<GateStructure>()
                .Include(gs => gs.Location)
                .Include(gs => gs.Street)
                .Include(gs => gs.District)
                .Include(gs => gs.IconMaterial)
                .Include(gs => gs.FallbackMaterial)
                .Include(gs => gs.AnchorPoint)
                .Include(gs => gs.OpenAnchorPoint)
                .Include(gs => gs.ReferencePoint1)
                .Include(gs => gs.ReferencePoint2)
                .Include(gs => gs.HingeAxis)
                .Include(gs => gs.LeftDoorSeedBlock)
                .Include(gs => gs.RightDoorSeedBlock)
                .Include(gs => gs.InfoDisplayLocation)
                .Include(gs => gs.GuardSpawnLocations);
        }
    }
}
