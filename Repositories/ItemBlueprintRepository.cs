using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class ItemBlueprintRepository : IItemBlueprintRepository
    {
        private readonly KnKDbContext _context;

        public ItemBlueprintRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<ItemBlueprint>> GetAllAsync()
        {
            return await _context.ItemBlueprints
                .Include(ib => ib.IconMaterial)
                .Include(ib => ib.Category)
                .Include(ib => ib.Grade)
                .Include(ib => ib.DefaultEnchantments)
                    .ThenInclude(de => de.EnchantmentDefinition)
                        .ThenInclude(ed => ed.BaseEnchantmentRef)
                .Include(ib => ib.Tags)
                    .ThenInclude(t => t.Tag)
                .Include(ib => ib.Origins)
                    .ThenInclude(o => o.Domain)
                .ToListAsync();
        }

        public async Task<ItemBlueprint?> GetByIdAsync(int id)
        {
            return await _context.ItemBlueprints
                .Include(ib => ib.IconMaterial)
                .Include(ib => ib.Category)
                .Include(ib => ib.Grade)
                .Include(ib => ib.DefaultEnchantments)
                    .ThenInclude(de => de.EnchantmentDefinition)
                        .ThenInclude(ed => ed.BaseEnchantmentRef)
                .Include(ib => ib.Tags)
                    .ThenInclude(t => t.Tag)
                .Include(ib => ib.Origins)
                    .ThenInclude(o => o.Domain)
                .FirstOrDefaultAsync(ib => ib.Id == id);
        }

        public async Task AddAsync(ItemBlueprint entity)
        {
            await _context.ItemBlueprints.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(ItemBlueprint entity)
        {
            _context.ItemBlueprints.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.ItemBlueprints.FindAsync(id);
            if (entity != null)
            {
                _context.ItemBlueprints.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<ItemBlueprint>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.ItemBlueprints.AsQueryable();

            // Apply search term filter
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(ib =>
                    ib.Name.ToLower().Contains(searchLower) ||
                    ib.Description.ToLower().Contains(searchLower) ||
                    ib.DefaultDisplayName.ToLower().Contains(searchLower));
            }

            // Menu follow-up 2026-09-26 (in-game item catalogue): filter by category. "Category" is a
            // category name (case-insensitive), "CategoryId" an id; either includes every
            // sub-category below it, so filtering on "Weapons" also shows "Swords".
            var categoryIds = await ResolveCategoryFilterAsync(query.Filters);
            if (categoryIds != null)
            {
                queryable = queryable.Where(ib => ib.CategoryId != null && categoryIds.Contains(ib.CategoryId.Value));
            }

            // Get total count before pagination
            var totalCount = await queryable.CountAsync();

            // Apply sorting
            queryable = query.SortBy switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(ib => ib.Name) : queryable.OrderBy(ib => ib.Name),
                "defaultDisplayName" => query.SortDescending ? queryable.OrderByDescending(ib => ib.DefaultDisplayName) : queryable.OrderBy(ib => ib.DefaultDisplayName),
                _ => query.SortDescending ? queryable.OrderByDescending(ib => ib.Id) : queryable.OrderBy(ib => ib.Id)
            };

            // Apply pagination
            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(ib => ib.IconMaterial)
                .Include(ib => ib.Category)
                .Include(ib => ib.Grade)
                .Include(ib => ib.DefaultEnchantments)
                .Include(ib => ib.Tags)
                .ToListAsync();

            return new PagedResult<ItemBlueprint>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        /// <summary>
        /// The category ids a "Category"/"CategoryId" filter selects (the named category and all its
        /// descendants), or null when neither filter is set. An unknown category selects nothing.
        /// </summary>
        private async Task<HashSet<int>?> ResolveCategoryFilterAsync(Dictionary<string, string>? filters)
        {
            if (filters == null) return null;
            string? name = null;
            int? id = null;
            foreach (var (key, value) in filters)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (key.Equals("Category", StringComparison.OrdinalIgnoreCase)) name = value.Trim();
                else if (key.Equals("CategoryId", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var parsed)) id = parsed;
            }
            if (name == null && id == null) return null;

            var categories = await _context.Categories
                .Select(c => new { c.Id, c.Name, c.ParentCategoryId })
                .ToListAsync();
            var roots = categories
                .Where(c => (id != null && c.Id == id) || (name != null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.Id)
                .ToList();
            var selected = new HashSet<int>(roots);
            var frontier = new Queue<int>(roots);
            while (frontier.Count > 0)
            {
                var parent = frontier.Dequeue();
                foreach (var child in categories.Where(c => c.ParentCategoryId == parent))
                {
                    if (selected.Add(child.Id)) frontier.Enqueue(child.Id);
                }
            }
            return selected;
        }
    }
}
