using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class LootboxSpecialEntryRepository : ILootboxSpecialEntryRepository
    {
        private readonly KnKDbContext _context;

        public LootboxSpecialEntryRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<LootboxSpecialEntry> WithIncludes() => _context.LootboxSpecialEntries
            .Include(s => s.LootboxType)
            .Include(s => s.ItemBlueprint);

        public async Task<IEnumerable<LootboxSpecialEntry>> GetAllAsync()
        {
            return await WithIncludes().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        }

        public async Task<LootboxSpecialEntry?> GetByIdAsync(int id)
        {
            return await WithIncludes().FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task AddAsync(LootboxSpecialEntry entity)
        {
            await _context.LootboxSpecialEntries.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(LootboxSpecialEntry entity)
        {
            _context.LootboxSpecialEntries.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.LootboxSpecialEntries.FindAsync(id);
            if (entity != null)
            {
                _context.LootboxSpecialEntries.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<LootboxSpecialEntry>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.LootboxSpecialEntries.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(s =>
                    (s.ItemBlueprint.Name != null && s.ItemBlueprint.Name.ToLower().Contains(searchLower)) ||
                    (s.LootboxType != null && s.LootboxType.Name.ToLower().Contains(searchLower)));
            }

            var totalCount = await queryable.CountAsync();

            queryable = query.SortBy switch
            {
                "chancePerMillion" => query.SortDescending ? queryable.OrderByDescending(s => s.ChancePerMillion) : queryable.OrderBy(s => s.ChancePerMillion),
                "sortOrder" => query.SortDescending ? queryable.OrderByDescending(s => s.SortOrder) : queryable.OrderBy(s => s.SortOrder),
                _ => query.SortDescending ? queryable.OrderByDescending(s => s.Id) : queryable.OrderBy(s => s.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(s => s.LootboxType)
                .Include(s => s.ItemBlueprint)
                .ToListAsync();

            return new PagedResult<LootboxSpecialEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<bool> TypeExistsAsync(int typeId)
        {
            return await _context.LootboxTypes.AnyAsync(t => t.Id == typeId);
        }

        public async Task<bool> BlueprintExistsAsync(int blueprintId)
        {
            return await _context.ItemBlueprints.AnyAsync(b => b.Id == blueprintId);
        }

        public async Task EnsureSpecialTagAsync(int blueprintId)
        {
            var tag = await _context.Tags.OrderBy(t => t.Id).FirstOrDefaultAsync(t => t.Name == LootboxSeed.SpecialTag);
            if (tag == null)
            {
                tag = new Tag { Name = LootboxSeed.SpecialTag };
                _context.Tags.Add(tag);
            }
            else if (await _context.Set<ItemBlueprintTag>().AnyAsync(t => t.ItemBlueprintId == blueprintId && t.TagId == tag.Id))
            {
                return;
            }
            _context.Set<ItemBlueprintTag>().Add(new ItemBlueprintTag { ItemBlueprintId = blueprintId, Tag = tag });
        }
    }
}
