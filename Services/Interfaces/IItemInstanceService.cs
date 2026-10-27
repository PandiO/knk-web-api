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

        /// <summary>
        /// <c>/knk item more</c> (PandiO/knk-workspace#175): mints <paramref name="count"/> new instances copied from
        /// instance <paramref name="sourceId"/> - same blueprint, grade (frozen at the source's mint), enchantments,
        /// custom display name and flags - with <c>Origin = Admin</c>, <c>OriginRef = "clone:&lt;sourceId&gt;"</c>,
        /// <c>OwnerCount = 1</c> and a new CreatedAt. The owner is <paramref name="ownerUserId"/> when given, else the
        /// source's owner. Saved together (all or none) and audited as <see cref="Enums.AuditAction.ItemInstanceCloned"/>.
        /// Throws <see cref="KeyNotFoundException"/> for an unknown source or owner, <see cref="ArgumentException"/>
        /// for a count outside 1..<see cref="MaxCloneCount"/>, <see cref="InvalidOperationException"/> when the
        /// source's blueprint has become stackable (stackable items get no instance).
        /// </summary>
        Task<ItemInstanceCloneResultDto> CloneAsync(long sourceId, int count, int? ownerUserId, int? actorUserId);

        /// <summary>The most instances one clone call mints (a full stack of a stackable item).</summary>
        public const int MaxCloneCount = 64;
    }
}
