using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>One enchantment to put on a new instance.</summary>
    public sealed record ItemInstanceEnchantmentSpec(int EnchantmentDefinitionId, int Level);

    /// <summary>
    /// The minimal ItemInstance (vision §9.1; docs/specs/lootboxes/DESIGN.md §3.2, D16). Instances are minted by
    /// other services inside their own transaction - there is no create/update endpoint.
    /// </summary>
    public interface IItemInstanceService
    {
        /// <summary>
        /// Builds, but does not add or save, an instance of a non-stackable <paramref name="blueprint"/>
        /// (<c>MaxStackSize == 1</c>) with <c>OwnerCount = 1</c>, both flags false and the given enchantments
        /// (one row per definition; a duplicate keeps the higher level). The caller adds it to its context and
        /// saves it in its own transaction. Throws <see cref="InvalidOperationException"/> for a stackable
        /// blueprint and <see cref="ArgumentException"/> for an unknown definition or a level below 1.
        /// </summary>
        Task<ItemInstance> BuildAsync(
            ItemBlueprint blueprint,
            int? gradeId,
            int? ownerUserId,
            ItemInstanceOrigin origin,
            IEnumerable<ItemInstanceEnchantmentSpec> enchantments,
            string? originRef = null);

        Task<ItemInstanceDto?> GetAsync(long id);
    }
}
