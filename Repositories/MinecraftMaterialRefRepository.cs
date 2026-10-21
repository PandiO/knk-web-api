using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class MinecraftMaterialRefRepository : IMinecraftMaterialRefRepository
    {
        private readonly KnKDbContext _context;

        public MinecraftMaterialRefRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<MinecraftMaterialRef>> GetAllAsync()
        {
            return await _context.MinecraftMaterialRefs.ToListAsync();
        }

        public async Task<MinecraftMaterialRef?> GetByIdAsync(int id)
        {
            return await _context.MinecraftMaterialRefs.FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<MinecraftMaterialRef?> GetByNamespaceKeyAsync(string namespaceKey)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey)) return null;
            var key = namespaceKey.ToLower();
            return await _context.MinecraftMaterialRefs.FirstOrDefaultAsync(x => x.NamespaceKey.ToLower() == key);
        }

        public async Task AddAsync(MinecraftMaterialRef entity)
        {
            await _context.MinecraftMaterialRefs.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public void Detach(MinecraftMaterialRef entity)
        {
            _context.Entry(entity).State = EntityState.Detached;
        }

        public async Task UpdateAsync(MinecraftMaterialRef entity)
        {
            _context.MinecraftMaterialRefs.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var existing = await _context.MinecraftMaterialRefs.FindAsync(id);
            if (existing != null)
            {
                _context.MinecraftMaterialRefs.Remove(existing);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<MinecraftMaterialRef>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.MinecraftMaterialRefs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.ToLower();
                queryable = queryable.Where(x => x.NamespaceKey.ToLower().Contains(term)
                                              || (x.DisplayName != null && x.DisplayName.ToLower().Contains(term))
                                              || (x.LegacyName != null && x.LegacyName.ToLower().Contains(term))
                                              || x.Category.ToLower().Contains(term));
            }

            if (query.Filters != null)
            {
                if (query.Filters.TryGetValue("category", out var category) && !string.IsNullOrWhiteSpace(category))
                {
                    queryable = queryable.Where(x => x.Category.ToLower() == category.ToLower());
                }
                // KNG-200 phase 4 picker filters (MinecraftPickerFilters). A row no scan has seen yet has no
                // flags; its legacy category decides.
                if (query.Filters.TryGetValue("kind", out var kind) && !string.IsNullOrWhiteSpace(kind))
                {
                    if (kind.Equals("item", StringComparison.OrdinalIgnoreCase))
                    {
                        queryable = queryable.Where(x => x.IsItem == true || (x.IsItem == null && x.Category != "BLOCK"));
                    }
                    else if (kind.Equals("block", StringComparison.OrdinalIgnoreCase))
                    {
                        queryable = queryable.Where(x => x.IsBlock == true || (x.IsBlock == null && x.Category == "BLOCK"));
                    }
                }
                if (query.Filters.TryGetValue("keyContains", out var keyContains) && !string.IsNullOrWhiteSpace(keyContains))
                {
                    var part = keyContains.ToLower();
                    queryable = queryable.Where(x => x.NamespaceKey.ToLower().Contains(part));
                }
                if (query.Filters.TryGetValue("excludeMissing", out var excludeMissing) && excludeMissing.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    queryable = queryable.Where(x => x.CatalogStatus != MinecraftCatalogStatus.Missing);
                }
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();
            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<MinecraftMaterialRef>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private IQueryable<MinecraftMaterialRef> ApplySorting(IQueryable<MinecraftMaterialRef> queryable, string? sortBy, bool sortDescending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
            {
                return queryable.OrderBy(x => x.NamespaceKey);
            }

            return sortBy.ToLower() switch
            {
                "namespacekey" => sortDescending ? queryable.OrderByDescending(x => x.NamespaceKey) : queryable.OrderBy(x => x.NamespaceKey),
                "displayname" or "name" => sortDescending
                    ? queryable.OrderByDescending(x => x.DisplayName ?? x.NamespaceKey).ThenByDescending(x => x.NamespaceKey)
                    : queryable.OrderBy(x => x.DisplayName ?? x.NamespaceKey).ThenBy(x => x.NamespaceKey),
                "category" => sortDescending ? queryable.OrderByDescending(x => x.Category) : queryable.OrderBy(x => x.Category),
                "id" => sortDescending ? queryable.OrderByDescending(x => x.Id) : queryable.OrderBy(x => x.Id),
                _ => queryable.OrderBy(x => x.NamespaceKey)
            };
        }
    }
}
