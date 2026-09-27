using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>
    /// Data access for the lootbox runtime (docs/specs/lootboxes/DESIGN.md §3.3): spawns, claims and the reads the
    /// spawn and claim rules need. The transaction and row-lock helpers are no-ops on non-relational providers (EF
    /// InMemory in tests).
    /// </summary>
    public interface ILootboxRuntimeRepository
    {
        /// <summary>
        /// Runs <paramref name="work"/> in a READ COMMITTED transaction (unless one is already open) and commits it;
        /// rolls back and rethrows on any exception. READ COMMITTED so a count taken after a row lock sees what the
        /// lock holder committed.
        /// </summary>
        Task<T> InTransactionAsync<T>(Func<Task<T>> work);

        /// <summary>Forgets every tracked change, e.g. the rows of a claim whose save lost a race.</summary>
        void DiscardChanges();

        Task SaveChangesAsync();

        // ===== Configuration, types, grades, areas =====

        Task<LootboxConfiguration?> GetConfigurationAsync();

        /// <summary>SELECT … FOR UPDATE on the configuration singleton inside the open transaction: serializes spawns
        /// so two requests can't both take the last free slot.</summary>
        Task LockConfigurationAsync();

        /// <summary>Enabled types with category (and its icon), display material and grade weights.</summary>
        Task<List<LootboxType>> GetEnabledTypesAsync();

        /// <summary>One type with category and grade weights, enabled or not.</summary>
        Task<LootboxType?> GetTypeAsync(int id);

        Task<List<Grade>> GetGradesAsync();

        /// <summary>All areas with their allowed types, ordered by name.</summary>
        Task<List<LootboxSpawnArea>> GetAreasAsync();

        Task<LootboxSpawnArea?> GetAreaAsync(int id);

        // ===== Spawns =====

        /// <summary>The lazy expiry sweep: Active spawns whose ExpiresAt &lt;= <paramref name="now"/> become
        /// Expired. Returns how many changed.</summary>
        Task<int> ExpireDueAsync(DateTime now);

        /// <summary>Active spawns, optionally only one area's.</summary>
        Task<int> CountActiveAsync(int? areaId = null);

        /// <summary>Active spawns per area id (areas without any are absent).</summary>
        Task<Dictionary<int, int>> CountActiveByAreaAsync();

        /// <summary>Active spawns with type (and category), box grade and area, oldest first. No tracking.</summary>
        Task<List<LootboxSpawn>> GetActiveSpawnsAsync();

        /// <summary>One spawn with type (and category), box grade and area. Tracked.</summary>
        Task<LootboxSpawn?> GetSpawnAsync(int id);

        /// <summary>The spawn's current status straight from the store (no tracking), or null.</summary>
        Task<LootboxSpawnStatus?> GetSpawnStatusAsync(int id);

        Task AddSpawnAsync(LootboxSpawn spawn);

        // ===== Claims =====

        /// <summary>One claim with everything its result needs: type, grades, blueprint (default enchantments),
        /// user, and the instance's enchantments (definitions). No tracking.</summary>
        Task<LootboxClaim?> GetClaimAsync(int id);

        /// <summary>The claim stored under <paramref name="idempotencyKey"/>, or null. No tracking.</summary>
        Task<LootboxClaim?> GetClaimByIdempotencyKeyAsync(string idempotencyKey);

        Task<bool> SpawnHasClaimAsync(int spawnId);

        /// <summary>
        /// How many world-box claims and token redeems <paramref name="userId"/> made with ClaimedAt in
        /// [<paramref name="from"/>, <paramref name="to"/>), optionally of one type. Admin gives (neither a spawn nor
        /// a token) are not counted (DESIGN.md §3.3 step 3; a token redeem is a claim like any other, Phase 5).
        /// </summary>
        Task<int> CountClaimsAsync(int userId, DateTime from, DateTime to, int? lootboxTypeId = null);

        /// <summary>Adds a claim (and, through its navigation, its new ItemInstance) without saving.</summary>
        void AddClaim(LootboxClaim claim);

        /// <summary>A claim for a delivery update. Tracked.</summary>
        Task<LootboxClaim?> GetClaimForUpdateAsync(int id);

        /// <summary>The user's undelivered claims made at or before <paramref name="claimedAtOrBefore"/>, oldest
        /// first, loaded like <see cref="GetClaimAsync"/>.</summary>
        Task<List<LootboxClaim>> GetPendingAsync(int userId, DateTime claimedAtOrBefore);

        /// <summary>The paged drop log. Filters: userId, lootboxTypeId, itemGradeId, boxGradeId, isSpecial, delivered,
        /// adminGive, source (World|Token|AdminGive), from, to (UTC ISO dates); SearchTerm matches the username or the
        /// item name.</summary>
        Task<PagedResult<LootboxClaim>> SearchClaimsAsync(PagedQuery query);

        // ===== Token items (Phase 5) =====

        /// <summary>Adds new tokens without saving.</summary>
        void AddTokens(IEnumerable<LootboxToken> tokens);

        /// <summary>One token by its item id with type (category), box grade, users and claim. Tracked.</summary>
        Task<LootboxToken?> GetTokenAsync(Guid token);

        /// <summary>The token's row id without loading or tracking it, or null for an unknown token.</summary>
        Task<int?> FindTokenIdAsync(Guid token);

        /// <summary>The token's current status straight from the store (no tracking), or null.</summary>
        Task<LootboxTokenStatus?> GetTokenStatusAsync(int id);

        Task<bool> TokenHasClaimAsync(int tokenId);

        /// <summary>The tokens one issue request created, by IssueIndex. No tracking.</summary>
        Task<List<LootboxToken>> GetTokensByIssueKeyAsync(string issueKey);

        /// <summary>Tokens by id, loaded like <see cref="GetTokenAsync"/>. No tracking.</summary>
        Task<List<LootboxToken>> GetTokensAsync(IEnumerable<int> ids);

        /// <summary>The user's Issued tokens the plugin hasn't handed over yet, oldest first. No tracking.</summary>
        Task<List<LootboxToken>> GetUndeliveredTokensAsync(int userId);

        /// <summary>The given tokens that were issued to <paramref name="userId"/> and aren't delivered yet. Tracked.</summary>
        Task<List<LootboxToken>> GetTokensForDeliveryAsync(int userId, IEnumerable<Guid> tokens);

        /// <summary>Paged tokens. Filters: userId (issued to or redeemed by), lootboxTypeId, status, reason, delivered;
        /// SearchTerm is a token id or part of a username. Newest first by default.</summary>
        Task<PagedResult<LootboxToken>> SearchTokensAsync(PagedQuery query);

        /// <summary>The token picked up from a world box (DESIGN.md §3.8), with its includes; null when nobody took it.</summary>
        Task<LootboxToken?> GetTokenBySourceSpawnAsync(int spawnId);

        /// <summary>World boxes picked up by the user in [from, to), optionally of one type (the daily pickup cap).</summary>
        Task<int> CountPickupsAsync(int userId, DateTime from, DateTime to, int? lootboxTypeId = null);

        /// <summary>Status by token id for the given ids; unknown ids are left out.</summary>
        Task<Dictionary<Guid, LootboxTokenStatus>> GetTokenStatusesAsync(IEnumerable<Guid> tokens);

        /// <summary>A blueprint as the claim needs it (MaxStackSize, default enchantments). No tracking.</summary>
        Task<ItemBlueprint?> GetBlueprintAsync(int id);
    }
}
