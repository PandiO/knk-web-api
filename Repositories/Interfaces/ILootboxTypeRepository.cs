using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface ILootboxTypeRepository
    {
        Task<IEnumerable<LootboxType>> GetAllAsync();

        /// <summary>One type with its category, display material, grade weights, pool entries (blueprints) and
        /// enchant rolls (definitions).</summary>
        Task<LootboxType?> GetByIdAsync(int id);

        Task AddAsync(LootboxType entity);
        Task UpdateAsync(LootboxType entity);
        Task DeleteAsync(int id);
        Task<PagedResult<LootboxType>> SearchAsync(PagedQuery query);

        /// <summary>True when another type (not <paramref name="excludeTypeId"/>) already uses the category.</summary>
        Task<bool> CategoryTakenAsync(int categoryId, int? excludeTypeId = null);

        /// <summary>Why the type can't be deleted (special entries, spawns or claims reference it), or null.</summary>
        Task<string?> FindDeleteBlockerAsync(int id);

        // --- Reads for validation and the roll input ---

        Task<List<Category>> GetCategoriesAsync();
        Task<List<Grade>> GetGradesAsync();
        Task<bool> MaterialExistsAsync(int materialRefId);
        Task<HashSet<int>> GetExistingBlueprintIdsAsync(IEnumerable<int> blueprintIds);
        Task<Dictionary<int, EnchantmentDefinition>> GetEnchantmentDefinitionsAsync(IEnumerable<int> definitionIds);

        /// <summary>
        /// Blueprints in any of <paramref name="categoryIds"/> plus the ones in <paramref name="extraBlueprintIds"/>,
        /// with icon material, tags and default enchantments (definitions) loaded. No tracking.
        /// </summary>
        Task<List<ItemBlueprint>> GetPoolBlueprintsAsync(ICollection<int> categoryIds, ICollection<int> extraBlueprintIds);

        /// <summary>Enabled special entries for the type or for any type, with their blueprints loaded as above.</summary>
        Task<List<LootboxSpecialEntry>> GetApplicableSpecialsAsync(int typeId);
    }
}
