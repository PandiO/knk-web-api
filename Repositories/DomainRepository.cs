using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class DomainRepository : IDomainRepository
    {
        private readonly KnKDbContext _context;

        public DomainRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Domain>> GetAllAsync()
        {
            // Domain has no ParentDomain navigation; return plain list
            return await _context.Domains.ToListAsync();
        }

        public async Task<Domain?> GetByIdAsync(int id)
        {
            // Domain has no ParentDomain navigation; query by Id only
            return await _context.Domains.FirstOrDefaultAsync(d => d.Id == id);
        }

        public Task<Domain?> GetByWgRegionNameAsync(string regionName) => GetByWgRegionNameAsync(regionName, null);

        public async Task<Domain?> GetByWgRegionNameAsync(string regionName, string? worldName) =>
            (await GetByAnyRegionAsync(regionName, worldName))?.Domain;

        public async Task<DomainRegionMatch?> GetByAnyRegionAsync(string regionName, string? worldName)
        {
            if (string.IsNullOrWhiteSpace(regionName)) return null;
            var v = regionName.ToLower().Trim();
            var candidates = _context.Domains
                .Where(d => d.WgRegionId != null && d.WgRegionId.ToLower() == v);

            Domain? domain;
            DomainSubRegion? matchedSubRegion = null;
            if (string.IsNullOrWhiteSpace(worldName))
            {
                // World-blind (pre-KNG-111 callers): the lowest id, so duplicates resolve the same way every time.
                domain = await candidates.OrderBy(d => d.Id).FirstOrDefaultAsync();
            }
            else
            {
                var w = worldName.ToLower().Trim();
                domain = await candidates
                    .Where(d => d.WorldName != null && d.WorldName.ToLower() == w)
                    .OrderBy(d => d.Id)
                    .FirstOrDefaultAsync()
                    // Not assigned a world yet (created before KNG-111 and not backfilled): still found.
                    ?? await candidates
                        .Where(d => d.WorldName == null || d.WorldName == "")
                        .OrderBy(d => d.Id)
                        .FirstOrDefaultAsync();
            }

            if (domain == null)
            {
                // KNG-254: one of a domain's sub-regions (they live in their domain's world).
                matchedSubRegion = await FindSubRegionAsync(v, worldName);
                domain = matchedSubRegion?.Domain;
            }
            if (domain == null) return null;

            await _context.Entry(domain).Collection(d => d.SubRegions).LoadAsync();

            // Explicitly load the entire ParentDomain chain (unknown depth)
            Domain? current = domain;
            while (current != null)
            {
                if (current is Town) {
                    break;
                } else if (current is District district)
                {
                    await _context.Entry(district).Reference(d => d.Town).LoadAsync();
                    if (district.TownId <= 0) break;
                    current = district.Town;
                    continue;
                }  else if (current is Structure structure) {
                    await _context.Entry(structure).Reference(s => s.District).LoadAsync();
                    if (structure.DistrictId <= 0) break;
                    current = structure.District;
                    continue;
                }
                break;
            }

            return new DomainRegionMatch(domain, matchedSubRegion?.Id);
        }

        public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetSubRegionIdsByDomainAsync()
        {
            var rows = await _context.DomainSubRegions.AsNoTracking()
                .OrderBy(s => s.Id)
                .Select(s => new { s.DomainId, s.WgRegionId })
                .ToListAsync();
            return rows.GroupBy(r => r.DomainId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.WgRegionId).ToList());
        }

        private async Task<DomainSubRegion?> FindSubRegionAsync(string loweredRegion, string? worldName)
        {
            var candidates = _context.DomainSubRegions
                .Include(s => s.Domain)
                .Where(s => s.WgRegionId.ToLower() == loweredRegion);
            if (string.IsNullOrWhiteSpace(worldName))
            {
                return await candidates.OrderBy(s => s.Id).FirstOrDefaultAsync();
            }
            var w = worldName.ToLower().Trim();
            return await candidates
                .Where(s => s.Domain.WorldName != null && s.Domain.WorldName.ToLower() == w)
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync()
                ?? await candidates
                    .Where(s => s.Domain.WorldName == null || s.Domain.WorldName == "")
                    .OrderBy(s => s.Id)
                    .FirstOrDefaultAsync();
        }

        public async Task AddDomainAsync(Domain domain)
        {
            await _context.Domains.AddAsync(domain);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateDomainAsync(Domain domain)
        {
            _context.Domains.Update(domain);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteDomainAsync(int id)
        {
            var domain = await _context.Domains.FindAsync(id);
            if (domain != null)
            {
                _context.Domains.Remove(domain);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<Domain>> SearchAsync(PagedQuery query)
        {
            // Domain is a TPT base (Town/District/Structure/...); querying the base DbSet already
            // materializes each row into its concrete runtime subtype, which is all DomainMappingProfile's
            // GetType().Name convention (DomainListDto.DomainType) needs - no discriminator column, no
            // extra Include.
            var queryable = _context.Domains.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(d => d.Name.ToLower().Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();

            queryable = query.SortBy switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(d => d.Name) : queryable.OrderBy(d => d.Name),
                "id" => query.SortDescending ? queryable.OrderByDescending(d => d.Id) : queryable.OrderBy(d => d.Id),
                "createdat" => query.SortDescending ? queryable.OrderByDescending(d => d.CreatedAt) : queryable.OrderBy(d => d.CreatedAt),
                _ => queryable.OrderBy(d => d.Name)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<Domain>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<IReadOnlyDictionary<string, NavigationDestinationMode>> GetNavigationDefaultsAsync()
        {
            var rows = await _context.DomainNavigationDefaults.AsNoTracking().ToListAsync();
            return rows.ToDictionary(r => r.DomainType, r => r.DefaultMode, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<IReadOnlyDictionary<string, RoadAccessRule>> GetRoadAccessDefaultsAsync()
        {
            var rows = await _context.DomainNavigationDefaults.AsNoTracking().ToListAsync();
            return rows.ToDictionary(r => r.DomainType, r => r.RoadAccess, StringComparer.OrdinalIgnoreCase);
        }
    }
}
