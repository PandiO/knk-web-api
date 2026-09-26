using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Lootbox;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// The lootbox runtime (docs/specs/lootboxes/DESIGN.md §3.1, §3.3; IMPLEMENTATION_PLAN.md Phase 2): the API decides
    /// whether a box may spawn and what it is, rolls the item once when it is claimed, mints its ItemInstance in the
    /// same transaction, and keeps the drop log the plugin delivers from.
    /// <para>
    /// Errors: bad input → <see cref="ArgumentException"/> (400); unknown spawn/user/type/claim →
    /// <see cref="KeyNotFoundException"/> (404); a refused action → <see cref="LootboxConflictException"/> (409
    /// <c>{code}</c>); the daily cap → <see cref="LootboxDailyLimitException"/> (429).
    /// </para>
    /// </summary>
    public interface ILootboxRuntimeService
    {
        /// <summary>Enabled types, all grades, all areas (with their enabled flag and active count) and the global
        /// settings. Runs the lazy expiry sweep first.</summary>
        Task<LootboxRuntimeConfigDto> GetRuntimeConfigAsync();

        /// <summary>Active spawns after the lazy expiry sweep.</summary>
        Task<List<LootboxSpawnDto>> GetActiveAsync();

        /// <summary>
        /// A box in an area: checks the global switch, the area (enabled, same world), the area's MaxActive and the
        /// GlobalMaxActive, then rolls the type (SpawnWeight among the enabled types the area allows) and the box grade.
        /// 409 codes: Disabled, AreaFull, GlobalFull, NoEnabledType, NoBoxGrade.
        /// </summary>
        Task<LootboxSpawnDto> SpawnAsync(LootboxSpawnRequestDto request);

        /// <summary>A staff spawn: ignores caps, areas and the global switch; audited when the staff member is known.
        /// 409 EmptyPool when the type has nothing to give.</summary>
        Task<LootboxSpawnDto> AdminSpawnAsync(LootboxAdminSpawnRequestDto request, int? actorUserId);

        /// <summary>An Active box becomes Removed; any other status is returned unchanged.</summary>
        Task<LootboxSpawnDto> DespawnAsync(int spawnId, int? actorUserId);

        /// <summary>
        /// Opens a box (DESIGN.md §3.3 claim transaction). The same idempotency key replays the stored result
        /// (<c>replay=true</c>) without rolling again. 409 codes: TokenMismatch, AlreadyClaimed, Expired, Removed,
        /// Disabled, Frozen, UserInactive, EmptyPool, IdempotencyKeyReused; 429 DailyLimit (scope Global or Type).
        /// </summary>
        Task<LootboxClaimResultDto> ClaimAsync(int spawnId, LootboxClaimRequestDto request);

        /// <summary>Roll and mint without a world box (<c>LootboxSpawnId = null</c>); audited LootboxGranted; not
        /// counted against the daily cap.</summary>
        Task<LootboxClaimResultDto> AdminGiveAsync(LootboxAdminGiveRequestDto request, int? actorUserId);

        /// <summary>Sets DeliveredAt once; a repeat returns the first confirmation (<c>alreadyDelivered=true</c>).</summary>
        Task<LootboxDeliveredResultDto> MarkDeliveredAsync(int claimId, LootboxDeliveredRequestDto request);

        /// <summary>The user's undelivered claims older than <see cref="LootboxRuntimeServiceConstants.PendingGraceSeconds"/>
        /// seconds (a claim still being delivered is younger), same payload as the claim result.</summary>
        Task<List<LootboxClaimResultDto>> GetPendingAsync(int userId);

        Task<LootboxClaimResultDto?> GetClaimAsync(int claimId);

        /// <summary>The paged drop log for the web app.</summary>
        Task<PagedResultDto<LootboxClaimLogDto>> SearchClaimsAsync(PagedQueryDto query);
    }

    public static class LootboxRuntimeServiceConstants
    {
        public const int PendingGraceSeconds = 30;
        public const int MaxIdempotencyKeyLength = 128;
        public const int MaxDeliveryNoteLength = 512;
        public const int DefaultLifetimeMinutes = 30;
        public const int MaxLifetimeMinutes = 24 * 60;
    }
}
