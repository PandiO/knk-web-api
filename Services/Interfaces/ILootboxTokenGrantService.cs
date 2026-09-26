using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Lootbox token grant rules and the issue hooks that apply them (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md
    /// Phase 5): joining a premium tier (v1's donator rank boxes) or being granted a kit issues the rule's tokens to
    /// the player, and the plugin is told (PlayerNotification <c>LootboxTokensIssued</c>) to hand them over. PvP kill
    /// drops and referrals have no rule yet: their features call <c>POST api/LootboxTokens/issue</c> (reason
    /// <c>PvpKill</c>/<c>Referral</c>) directly.
    /// <para>
    /// The hooks never throw: a failed issue is logged and must not undo the rank change or kit grant that triggered
    /// it. Admin CRUD errors: bad input → <see cref="ArgumentException"/>, unknown rule → <see cref="KeyNotFoundException"/>.
    /// </para>
    /// </summary>
    public interface ILootboxTokenGrantService
    {
        Task<List<LootboxTokenGrantDto>> GetAllAsync();

        Task<LootboxTokenGrantDto> CreateAsync(LootboxTokenGrantDto dto);

        Task<LootboxTokenGrantDto> UpdateAsync(int id, LootboxTokenGrantDto dto);

        Task DeleteAsync(int id);

        /// <summary>The player just became a member of <paramref name="permissionGroupId"/> (a new or lapsed membership,
        /// not an extension). Issues the tier's tokens; returns how many.</summary>
        Task<int> IssueForPremiumTierAsync(int userId, int permissionGroupId, int? actorUserId);

        /// <summary>The player was granted a kit (claim, staff give or first-join grant, <paramref name="kitClaimId"/> =
        /// the KitClaim row, which keys the issue so it happens once). Issues the kit's tokens; returns how many.</summary>
        Task<int> IssueForKitAsync(int userId, int kitId, int kitClaimId, int? actorUserId);
    }
}
