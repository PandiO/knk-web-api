using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories;

public class MinecraftEnchantmentRefRepository : IMinecraftEnchantmentRefRepository
{
    private readonly KnKDbContext _context;

    public MinecraftEnchantmentRefRepository(KnKDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<MinecraftEnchantmentRef>> GetAllAsync()
    {
        return await _context.MinecraftEnchantmentRefs.ToListAsync();
    }

    public async Task<MinecraftEnchantmentRef?> GetByIdAsync(int id)
    {
        return await _context.MinecraftEnchantmentRefs.FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<MinecraftEnchantmentRef?> GetByNamespaceKeyAsync(string namespaceKey)
    {
        if (string.IsNullOrWhiteSpace(namespaceKey))
            return null;

        return await _context.MinecraftEnchantmentRefs
            .FirstOrDefaultAsync(e => e.NamespaceKey == namespaceKey);
    }

    public async Task AddAsync(MinecraftEnchantmentRef entity)
    {
        await _context.MinecraftEnchantmentRefs.AddAsync(entity);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(MinecraftEnchantmentRef entity)
    {
        _context.MinecraftEnchantmentRefs.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(MinecraftEnchantmentRef entity)
    {
        _context.MinecraftEnchantmentRefs.Remove(entity);
        await _context.SaveChangesAsync();
    }

    /// <summary>Database search (KNG-250): term over key, names and category; filters <c>origin</c>, <c>category</c>, <c>excludeMissing</c>.</summary>
    public async Task<PagedResult<MinecraftEnchantmentRef>> SearchAsync(PagedQuery query)
    {
        var queryable = _context.MinecraftEnchantmentRefs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.ToLower();
            queryable = queryable.Where(x => x.NamespaceKey.ToLower().Contains(term)
                                          || (x.DisplayName != null && x.DisplayName.ToLower().Contains(term))
                                          || (x.LegacyName != null && x.LegacyName.ToLower().Contains(term))
                                          || (x.Category != null && x.Category.ToLower().Contains(term)));
        }

        if (query.Filters != null)
        {
            if (query.Filters.TryGetValue("category", out var category) && !string.IsNullOrWhiteSpace(category))
            {
                queryable = queryable.Where(x => x.Category != null && x.Category.ToLower() == category.ToLower());
            }
            if (query.Filters.TryGetValue("origin", out var origin) && !string.IsNullOrWhiteSpace(origin))
            {
                if (origin.Equals("custom", StringComparison.OrdinalIgnoreCase)) queryable = queryable.Where(x => x.IsCustom);
                else if (origin.Equals("vanilla", StringComparison.OrdinalIgnoreCase)) queryable = queryable.Where(x => !x.IsCustom);
            }
            if (query.Filters.TryGetValue("excludeMissing", out var excludeMissing) && excludeMissing.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                queryable = queryable.Where(x => x.CatalogStatus != MinecraftCatalogStatus.Missing);
            }
        }

        var descending = query.SortDescending;
        queryable = (query.SortBy ?? string.Empty).ToLower() switch
        {
            "displayname" or "name" => descending
                ? queryable.OrderByDescending(x => x.DisplayName ?? x.NamespaceKey).ThenByDescending(x => x.NamespaceKey)
                : queryable.OrderBy(x => x.DisplayName ?? x.NamespaceKey).ThenBy(x => x.NamespaceKey),
            "category" => descending ? queryable.OrderByDescending(x => x.Category) : queryable.OrderBy(x => x.Category),
            "iscustom" => descending ? queryable.OrderByDescending(x => x.IsCustom) : queryable.OrderBy(x => x.IsCustom),
            "catalogstatus" => descending ? queryable.OrderByDescending(x => x.CatalogStatus) : queryable.OrderBy(x => x.CatalogStatus),
            "id" => descending ? queryable.OrderByDescending(x => x.Id) : queryable.OrderBy(x => x.Id),
            "namespacekey" when descending => queryable.OrderByDescending(x => x.NamespaceKey),
            _ => queryable.OrderBy(x => x.NamespaceKey),
        };

        var pageNumber = query.PageNumber > 0 ? query.PageNumber : 1;
        var pageSize = query.PageSize > 0 ? query.PageSize : 10;
        var totalCount = await queryable.CountAsync();
        var items = await queryable.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<MinecraftEnchantmentRef>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }
}
