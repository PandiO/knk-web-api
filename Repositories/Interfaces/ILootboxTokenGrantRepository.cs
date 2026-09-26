using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>Token grant rules (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 5): premium tier or kit → tokens.</summary>
    public interface ILootboxTokenGrantRepository
    {
        /// <summary>Every rule with its type, group and kit, ordered by id. No tracking.</summary>
        Task<List<LootboxTokenGrant>> GetAllAsync();

        /// <summary>One rule. Tracked.</summary>
        Task<LootboxTokenGrant?> GetByIdAsync(int id);

        /// <summary>The enabled rules of a premium tier (group), ordered by id. No tracking.</summary>
        Task<List<LootboxTokenGrant>> GetEnabledForGroupAsync(int permissionGroupId);

        /// <summary>The enabled rules of a kit, ordered by id. No tracking.</summary>
        Task<List<LootboxTokenGrant>> GetEnabledForKitAsync(int kitId);

        Task AddAsync(LootboxTokenGrant grant);

        Task UpdateAsync(LootboxTokenGrant grant);

        Task DeleteAsync(LootboxTokenGrant grant);

        Task<bool> TypeExistsAsync(int lootboxTypeId);

        Task<PermissionGroup?> GetPermissionGroupAsync(int permissionGroupId);

        Task<bool> KitExistsAsync(int kitId);
    }
}
