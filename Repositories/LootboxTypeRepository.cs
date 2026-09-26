using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Same CRUD + paged-search shape as KitRepository, plus the reads LootboxTypeService needs to validate a type
    // and to build its roll input (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 1).
    public class LootboxTypeRepository : ILootboxTypeRepository
    {
        private readonly KnKDbContext _context;

        public LootboxTypeRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<LootboxType> WithIncludes() => _context.LootboxTypes
            .Include(t => t.Category)
            .Include(t => t.DisplayMaterial)
            .Include(t => t.GradeWeights)
                .ThenInclude(w => w.Grade)
            .Include(t => t.PoolEntries)
                .ThenInclude(p => p.ItemBlueprint)
            .Include(t => t.PoolEntries)
                .ThenInclude(p => p.GradeOverride)
            .Include(t => t.EnchantRolls)
                .ThenInclude(r => r.EnchantmentDefinition);

        public async Task<IEnumerable<LootboxType>> GetAllAsync()
        {
            return await WithIncludes().OrderBy(t => t.Name).ToListAsync();
        }

        public async Task<LootboxType?> GetByIdAsync(int id)
        {
            return await WithIncludes().FirstOrDefaultAsync(t => t.Id == id);
        }

        public async Task AddAsync(LootboxType entity)
        {
            await _context.LootboxTypes.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(LootboxType entity)
        {
            _context.LootboxTypes.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.LootboxTypes.FindAsync(id);
            if (entity != null)
            {
                _context.LootboxTypes.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<LootboxType>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.LootboxTypes.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(t =>
                    t.Name.ToLower().Contains(searchLower) ||
                    t.Category.Name.ToLower().Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();

            queryable = query.SortBy switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(t => t.Name) : queryable.OrderBy(t => t.Name),
                "enabled" => query.SortDescending ? queryable.OrderByDescending(t => t.Enabled) : queryable.OrderBy(t => t.Enabled),
                "spawnWeight" => query.SortDescending ? queryable.OrderByDescending(t => t.SpawnWeight) : queryable.OrderBy(t => t.SpawnWeight),
                _ => query.SortDescending ? queryable.OrderByDescending(t => t.Id) : queryable.OrderBy(t => t.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(t => t.Category)
                .Include(t => t.DisplayMaterial)
                .ToListAsync();

            return new PagedResult<LootboxType>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<bool> CategoryTakenAsync(int categoryId, int? excludeTypeId = null)
        {
            return await _context.LootboxTypes.AnyAsync(t => t.CategoryId == categoryId && (excludeTypeId == null || t.Id != excludeTypeId));
        }

        public async Task<string?> FindDeleteBlockerAsync(int id)
        {
            var specials = await _context.LootboxSpecialEntries.CountAsync(s => s.LootboxTypeId == id);
            if (specials > 0) return $"{specials} special entr{(specials == 1 ? "y" : "ies")} use it";
            var spawns = await _context.LootboxSpawns.CountAsync(s => s.LootboxTypeId == id);
            if (spawns > 0) return $"{spawns} lootbox(es) of this type have spawned; disable it instead";
            var claims = await _context.LootboxClaims.CountAsync(c => c.LootboxTypeId == id);
            if (claims > 0) return $"{claims} claim(s) of this type are in the drop log; disable it instead";
            return null;
        }

        public async Task<List<Category>> GetCategoriesAsync()
        {
            return await _context.Categories.AsNoTracking().ToListAsync();
        }

        public async Task<List<Grade>> GetGradesAsync()
        {
            return await _context.Grades.AsNoTracking().OrderBy(g => g.Stars).ThenBy(g => g.Id).ToListAsync();
        }

        public async Task<bool> MaterialExistsAsync(int materialRefId)
        {
            return await _context.MinecraftMaterialRefs.AnyAsync(m => m.Id == materialRefId);
        }

        public async Task<HashSet<int>> GetExistingBlueprintIdsAsync(IEnumerable<int> blueprintIds)
        {
            var ids = blueprintIds.Distinct().ToList();
            if (ids.Count == 0) return new HashSet<int>();
            return (await _context.ItemBlueprints.Where(b => ids.Contains(b.Id)).Select(b => b.Id).ToListAsync()).ToHashSet();
        }

        public async Task<Dictionary<int, EnchantmentDefinition>> GetEnchantmentDefinitionsAsync(IEnumerable<int> definitionIds)
        {
            var ids = definitionIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, EnchantmentDefinition>();
            return await _context.EnchantmentDefinitions.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id);
        }

        private IQueryable<ItemBlueprint> BlueprintsWithRollData() => _context.ItemBlueprints
            .AsNoTracking()
            .Include(b => b.IconMaterial)
            .Include(b => b.Tags)
                .ThenInclude(t => t.Tag)
            .Include(b => b.DefaultEnchantments)
                .ThenInclude(e => e.EnchantmentDefinition);

        public async Task<List<ItemBlueprint>> GetPoolBlueprintsAsync(ICollection<int> categoryIds, ICollection<int> extraBlueprintIds)
        {
            return await BlueprintsWithRollData()
                .Where(b => (b.CategoryId != null && categoryIds.Contains(b.CategoryId.Value)) || extraBlueprintIds.Contains(b.Id))
                .ToListAsync();
        }

        public async Task<List<LootboxSpecialEntry>> GetApplicableSpecialsAsync(int typeId)
        {
            var entries = await _context.LootboxSpecialEntries
                .AsNoTracking()
                .Where(s => s.Enabled && (s.LootboxTypeId == null || s.LootboxTypeId == typeId))
                .ToListAsync();
            var blueprintIds = entries.Select(s => s.ItemBlueprintId).Distinct().ToList();
            var blueprints = await BlueprintsWithRollData().Where(b => blueprintIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id);
            foreach (var entry in entries)
            {
                entry.ItemBlueprint = blueprints[entry.ItemBlueprintId];
            }
            return entries;
        }
    }
}
