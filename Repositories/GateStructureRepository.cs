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
                .Include(gs => gs.GateDoors).ThenInclude(d => d.BlockSnapshots)
                .Include(gs => gs.GateDoors).ThenInclude(d => d.OpenedBlockSnapshots)
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
                // isActive/gateType/isOpened are now per-door fields (item 5's multi-door
                // support) - a structure matches if at least one of its doors matches.
                if (query.Filters.TryGetValue("isActive", out var isActiveStr) && bool.TryParse(isActiveStr, out var isActive))
                {
                    queryable = queryable.Where(gs => gs.GateDoors.Any(d => d.IsActive == isActive));
                }
                if (query.Filters.TryGetValue("gateType", out var gateType) &&
                    System.Enum.TryParse<GateType>(gateType, true, out var parsedGateType))
                {
                    queryable = queryable.Where(gs => gs.GateDoors.Any(d => d.GateType == parsedGateType));
                }
                if (query.Filters.TryGetValue("isOpened", out var isOpenedStr) && bool.TryParse(isOpenedStr, out var isOpened))
                {
                    var matchState = isOpened ? GateDoorOpenState.OPEN : GateDoorOpenState.CLOSED;
                    queryable = queryable.Where(gs => gs.GateDoors.Any(d => d.OpenedState == matchState));
                }
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Include(gs => gs.Location)
                .Include(gs => gs.Street)
                .Include(gs => gs.District)
                .Include(gs => gs.IconMaterial)
                .Include(gs => gs.GuardSpawnLocations)
                .Include(gs => gs.GateDoors)
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
                .Include(gs => gs.GuardSpawnLocations)
                .Include(gs => gs.GateDoors).ThenInclude(d => d.AnchorPoint)
                .Include(gs => gs.GateDoors).ThenInclude(d => d.OpenAnchorPoint)
                .Include(gs => gs.GateDoors).ThenInclude(d => d.ReferencePoint1)
                .Include(gs => gs.GateDoors).ThenInclude(d => d.ReferencePoint2)
                .Include(gs => gs.GateDoors).ThenInclude(d => d.HingeAxis)
                .Include(gs => gs.GateDoors).ThenInclude(d => d.LeftDoorSeedBlock)
                .Include(gs => gs.GateDoors).ThenInclude(d => d.RightDoorSeedBlock)
                .Include(gs => gs.GateDoors).ThenInclude(d => d.InfoDisplayLocation)
                .Include(gs => gs.GateDoors).ThenInclude(d => d.FallbackMaterial);
        }
    }
}
