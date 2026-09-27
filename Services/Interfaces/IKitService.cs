using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Kit CRUD (FormWizard-only, docs/specs/kits/DESIGN.md §4.0) plus the grant/claim/purchase
    /// methods every real surface (in-game commands, first-join hook, web-app "Grant Kit" action)
    /// calls into - there is exactly one implementation of each, per DESIGN.md §4's "one grant
    /// path" principle.
    /// </summary>
    public interface IKitService
    {
        Task<IEnumerable<KitDto>> GetAllAsync();
        Task<KitDto?> GetByIdAsync(int id);
        Task<KitDto> CreateAsync(KitDto dto);
        Task UpdateAsync(int id, KitDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<KitDto>> SearchAsync(PagedQueryDto query);

        /// <summary>Every Kit, annotated with whether userId could claim it right now and why
        /// not if not (DESIGN.md §4.1) - what /kit list and the player-profile Grant Kit UI both
        /// render from.</summary>
        Task<List<KitAvailabilityDto>> GetAvailableForUserAsync(int userId);

        /// <summary>Re-validates gating + cooldown + purchase state + cost, atomically pays the
        /// cost (if any) as a KIT_CLAIM_COST ledger posting, writes a KitClaim row, and returns
        /// the resolved loadout. Never trusts a prior GetAvailableForUserAsync result (DESIGN.md
        /// §4.1). <paramref name="costContext"/> carries the caller and its Idempotency-Key
        /// (posted as <c>kit-claim:{key}</c>); required when the kit has a cost. A retry with the
        /// same key returns success without a second claim or payment.</summary>
        Task<KitClaimResultDto> ClaimKitAsync(int userId, int kitId, CurrencyContext? costContext = null);

        /// <summary>For IsSinglePurchasePremium kits only: pays PremiumPriceGems as a KIT_PURCHASE
        /// ledger posting (key <c>kit-purchase:{kitId}:{userId}</c>) and writes a KitPurchase row.
        /// Does not itself grant the kit (DESIGN.md §4.1/§5.2). <paramref name="purchaseContext"/>
        /// names the caller (initiator) only; the key is always the deterministic one.</summary>
        Task<KitPurchaseResultDto> PurchaseKitAsync(int userId, int kitId, CurrencyContext? purchaseContext = null);

        /// <summary>Staff-initiated grant - bypasses gating, cooldown, and cost entirely by
        /// design (DESIGN.md §4.1, §0b). Still writes a normal KitClaim row.</summary>
        Task<KitClaimResultDto> GiveKitAsync(int? actorUserId, int targetUserId, int kitId);

        /// <summary>Grants every GrantOnFirstJoin kit the user is gated to receive, ignoring
        /// cost/cooldown entirely (DESIGN.md §4.4).</summary>
        Task<List<KitClaimResultDto>> GrantFirstJoinKitsAsync(int userId);
    }
}
