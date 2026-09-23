using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class EnchantmentDefinitionRepository : IEnchantmentDefinitionRepository
    {
        private readonly KnKDbContext _context;

        public EnchantmentDefinitionRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<EnchantmentDefinition>> GetAllAsync()
        {
            return await _context.EnchantmentDefinitions
                .Include(ed => ed.BaseEnchantmentRef)
                .Include(ed => ed.AbilityDefinition)
                .Include(ed => ed.DefaultForBlueprints)
                    .ThenInclude(df => df.ItemBlueprint)
                .ToListAsync();
        }

        public async Task<EnchantmentDefinition?> GetByIdAsync(int id)
        {
            return await _context.EnchantmentDefinitions
                .Include(ed => ed.BaseEnchantmentRef)
                .Include(ed => ed.AbilityDefinition)
                .Include(ed => ed.DefaultForBlueprints)
                    .ThenInclude(df => df.ItemBlueprint)
                .FirstOrDefaultAsync(ed => ed.Id == id);
        }

        public async Task AddAsync(EnchantmentDefinition entity)
        {
            await _context.EnchantmentDefinitions.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(EnchantmentDefinition entity)
        {
            _context.EnchantmentDefinitions.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.EnchantmentDefinitions.FindAsync(id);
            if (entity != null)
            {
                _context.EnchantmentDefinitions.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<EnchantmentDefinition>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.EnchantmentDefinitions.AsQueryable();

            // Apply search term filter
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(ed =>
                    ed.Key.ToLower().Contains(searchLower) ||
                    ed.DisplayName.ToLower().Contains(searchLower) ||
                    ed.Description.ToLower().Contains(searchLower));
            }

            // Apply filters from dictionary (same convention as CategoryRepository/GateStructureRepository/
            // etc.) - this was previously missing entirely, so any caller passing Filters instead of
            // SearchTerm (e.g. knk-plugin's /knk enchantments apply, resolving a specific enchantment by
            // exact Key) silently got back every row, unfiltered. Case-insensitive key lookup since
            // callers vary between PascalCase ("Key", from the plugin) and camelCase (other repos'
            // established convention). Id/Key are exact matches (resolving one specific enchantment must
            // not be ambiguous); DisplayName stays Contains, matching /knk enchantments search's
            // discovery-oriented usage.
            if (query.Filters != null)
            {
                var filters = new Dictionary<string, string>(query.Filters, StringComparer.OrdinalIgnoreCase);

                if (filters.TryGetValue("Id", out var idStr) && int.TryParse(idStr, out var idValue))
                {
                    queryable = queryable.Where(ed => ed.Id == idValue);
                }

                if (filters.TryGetValue("Key", out var keyValue) && !string.IsNullOrWhiteSpace(keyValue))
                {
                    var keyLower = keyValue.ToLower();
                    queryable = queryable.Where(ed => ed.Key.ToLower() == keyLower);
                }

                if (filters.TryGetValue("DisplayName", out var displayNameValue) && !string.IsNullOrWhiteSpace(displayNameValue))
                {
                    var displayNameLower = displayNameValue.ToLower();
                    queryable = queryable.Where(ed => ed.DisplayName.ToLower().Contains(displayNameLower));
                }
            }

            // Get total count before pagination
            var totalCount = await queryable.CountAsync();

            // Apply sorting
            queryable = query.SortBy switch
            {
                "key" => query.SortDescending ? queryable.OrderByDescending(ed => ed.Key) : queryable.OrderBy(ed => ed.Key),
                "displayName" => query.SortDescending ? queryable.OrderByDescending(ed => ed.DisplayName) : queryable.OrderBy(ed => ed.DisplayName),
                "maxLevel" => query.SortDescending ? queryable.OrderByDescending(ed => ed.MaxLevel) : queryable.OrderBy(ed => ed.MaxLevel),
                _ => query.SortDescending ? queryable.OrderByDescending(ed => ed.Id) : queryable.OrderBy(ed => ed.Id)
            };

            // Apply pagination
            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(ed => ed.BaseEnchantmentRef)
                .Include(ed => ed.AbilityDefinition)
                .Include(ed => ed.DefaultForBlueprints)
                .ToListAsync();

            return new PagedResult<EnchantmentDefinition>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }
    }
}
