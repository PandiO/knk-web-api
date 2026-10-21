using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class MinecraftBlockRefRepository : IMinecraftBlockRefRepository
    {
        private readonly KnKDbContext _context;

        public MinecraftBlockRefRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<MinecraftBlockRef>> GetAllAsync()
        {
            return await _context.MinecraftBlockRefs.ToListAsync();
        }

        public async Task<MinecraftBlockRef?> GetByIdAsync(int id)
        {
            return await _context.MinecraftBlockRefs.FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task AddAsync(MinecraftBlockRef entity)
        {
            await _context.MinecraftBlockRefs.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(MinecraftBlockRef entity)
        {
            _context.MinecraftBlockRefs.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var existing = await _context.MinecraftBlockRefs.FindAsync(id);
            if (existing != null)
            {
                _context.MinecraftBlockRefs.Remove(existing);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<MinecraftBlockRef>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.MinecraftBlockRefs.Include(x => x.MaterialRef).AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.ToLower();
                queryable = queryable.Where(x => x.NamespaceKey.ToLower().Contains(term)
                                              || (x.LogicalType != null && x.LogicalType.ToLower().Contains(term))
                                              || (x.BlockStateString != null && x.BlockStateString.ToLower().Contains(term)));
            }

            if (query.Filters != null)
            {
                if (query.Filters.TryGetValue("logicalType", out var logicalType) && !string.IsNullOrWhiteSpace(logicalType))
                {
                    queryable = queryable.Where(x => x.LogicalType != null && x.LogicalType.ToLower() == logicalType.ToLower());
                }
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();
            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<MinecraftBlockRef>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private IQueryable<MinecraftBlockRef> ApplySorting(IQueryable<MinecraftBlockRef> queryable, string? sortBy, bool sortDescending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
            {
                return queryable.OrderBy(x => x.NamespaceKey);
            }

            return sortBy.ToLower() switch
            {
                "namespacekey" => sortDescending ? queryable.OrderByDescending(x => x.NamespaceKey) : queryable.OrderBy(x => x.NamespaceKey),
                "logicaltype" => sortDescending ? queryable.OrderByDescending(x => x.LogicalType) : queryable.OrderBy(x => x.LogicalType),
                "id" => sortDescending ? queryable.OrderByDescending(x => x.Id) : queryable.OrderBy(x => x.Id),
                _ => queryable.OrderBy(x => x.NamespaceKey)
            };
        }
    }
}
