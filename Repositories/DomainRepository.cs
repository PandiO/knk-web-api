using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class DomainRepository : IDomainRepository
    {
        /// <summary>
        /// Search filter (landmark domain LD20, PandiO/knk-workspace#984): the concrete domain types to list, comma
        /// separated, matched case-insensitively against <c>DomainListDto.DomainType</c> (the CLR type name). A type
        /// means that type only, not its subtypes (<c>Structure</c> leaves out <c>GateStructure</c>), so the filter and
        /// the type column always agree. Names that are no domain type match nothing; they never widen the list.
        /// </summary>
        public const string DomainTypeFilter = "domainType";

        /// <summary>
        /// Search filter (LD20): leaves out the domain with this id and every landmark below it (its child landmarks,
        /// their children, ...), for a landmark's parent picker. Only landmarks can have a landmark as parent, so the
        /// subtree is a landmark subtree. A value that is not a number is ignored, like the other id filters.
        /// </summary>
        public const string ExcludeSubtreeFilter = "excludeSubtreeOf";

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

            // The whole parent chain, through the one parent walk of every domain type (LD23, PandiO/knk-workspace#981).
            await knkwebapi_v2.Services.DomainParents.LoadAncestorsAsync(_context, domain);

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
            // GetType().Name convention (DomainListDto.DomainType) needs - no discriminator column.
            // The Location is included for DomainListDto.Location (destination engine E12, PandiO/knk-workspace#103).
            var queryable = _context.Domains.Include(d => d.Location).AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(d => d.Name.ToLower().Contains(searchLower));
            }

            if (query.Filters != null)
            {
                if (query.Filters.TryGetValue(DomainTypeFilter, out var typeNames) && !string.IsNullOrWhiteSpace(typeNames))
                {
                    queryable = queryable.Where(ExactTypeIn(ResolveDomainTypes(typeNames)));
                }
                if (query.Filters.TryGetValue(ExcludeSubtreeFilter, out var rootStr) && int.TryParse(rootStr, out var rootId))
                {
                    var excluded = await LandmarkSubtreeAsync(rootId);
                    queryable = queryable.Where(d => !excluded.Contains(d.Id));
                }
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

        /// <summary>The concrete domain types of the model named in a comma separated list (case-insensitive).</summary>
        private List<Type> ResolveDomainTypes(string typeNames)
        {
            var wanted = typeNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return DomainClrTypes().Where(t => wanted.Contains(t.Name)).ToList();
        }

        private List<Type> DomainClrTypes() => _context.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Where(t => typeof(Domain).IsAssignableFrom(t) && !t.IsAbstract)
            .Distinct()
            .ToList();

        /// <summary>
        /// <c>d =&gt; (d is T1 &amp;&amp; !(d is subtypes of T1)) || ...</c>: the row's runtime type is one of
        /// <paramref name="types"/> exactly. TPT type tests, translated by EF Core; no type matches nothing.
        /// </summary>
        private Expression<Func<Domain, bool>> ExactTypeIn(IReadOnlyCollection<Type> types)
        {
            var all = DomainClrTypes();
            var d = Expression.Parameter(typeof(Domain), "d");
            Expression? body = null;
            foreach (var type in types)
            {
                Expression term = type == typeof(Domain) ? Expression.Constant(true) : Expression.TypeIs(d, type);
                foreach (var sub in all.Where(s => s != type && type.IsAssignableFrom(s)))
                {
                    term = Expression.AndAlso(term, Expression.Not(Expression.TypeIs(d, sub)));
                }
                body = body == null ? term : Expression.OrElse(body, term);
            }
            return Expression.Lambda<Func<Domain, bool>>(body ?? Expression.Constant(false), d);
        }

        /// <summary>The domain <paramref name="rootId"/> and every landmark whose parent chain reaches it.</summary>
        private async Task<HashSet<int>> LandmarkSubtreeAsync(int rootId)
        {
            var links = await _context.Landmarks.AsNoTracking()
                .Where(l => l.ParentDomainId != null)
                .Select(l => new { l.Id, ParentId = l.ParentDomainId!.Value })
                .ToListAsync();
            var children = links.ToLookup(l => l.ParentId, l => l.Id);
            var result = new HashSet<int> { rootId };
            var queue = new Queue<int>();
            queue.Enqueue(rootId);
            while (queue.Count > 0)
            {
                foreach (var child in children[queue.Dequeue()])
                {
                    if (result.Add(child)) queue.Enqueue(child);
                }
            }
            return result;
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
