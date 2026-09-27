using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Lootbox;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Lootbox type CRUD with its grade weights, pool entries and enchant rolls (FormWizard), plus the odds preview
    /// and the roll input the runtime claim reuses (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 1). Validation
    /// failures throw <see cref="ArgumentException"/> (400); a second type for a category or deleting a type still
    /// in use throws <see cref="LootboxConflictException"/> (409).
    /// </summary>
    public interface ILootboxTypeService
    {
        Task<IEnumerable<LootboxTypeDto>> GetAllAsync();
        Task<LootboxTypeDto?> GetByIdAsync(int id);
        Task<LootboxTypeDto> CreateAsync(LootboxTypeDto dto);
        Task UpdateAsync(int id, LootboxTypeDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<LootboxTypeDto>> SearchAsync(PagedQueryDto query);

        /// <summary>
        /// The odds of a box of <paramref name="boxStars"/> (default: the type's MaxBoxStars), computed by
        /// <see cref="LootboxRollEngine.ComputeOdds"/> from the same input a real claim rolls with. Null when the type
        /// doesn't exist; <see cref="ArgumentException"/> when boxStars is outside 1-5.
        /// </summary>
        Task<LootboxOddsDto?> GetOddsAsync(int id, int? boxStars);

        /// <summary>The engine input for one type (grades, pool, enchant rolls, applicable specials); null when the
        /// type doesn't exist.</summary>
        Task<LootboxRollInput?> BuildRollInputAsync(int typeId);
    }
}
