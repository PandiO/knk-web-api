using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// A pending in-game moment for one player that the plugin has not shown yet - currently only
    /// a consolidated title change from AdjustBalancesAsync. Exists because a balance adjustment
    /// made through the web app returns its TitleChangeResultDto to the browser only; without this
    /// queue the Minecraft server never learned a promotion happened, so the player never saw the
    /// PromotionEffects sound/particles/message (the /knk user command only worked because the
    /// plugin itself was the caller and read the result straight off the HTTP response).
    /// </summary>
    public class PlayerNotificationDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        /// <summary>Minecraft UUID, null for a web-app-only account that never joined.</summary>
        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = null!;

        /// <summary>See PlayerNotificationTypes.</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = null!;

        /// <summary>Set when Type is TitleChanged.</summary>
        [JsonPropertyName("titleChange")]
        public TitleChangeResultDto? TitleChange { get; set; }

        /// <summary>Set when Type is PaymentReceived.</summary>
        [JsonPropertyName("payment")]
        public PaymentNotificationDto? Payment { get; set; }

        /// <summary>Set when Type is CurrencyAlert (then UserId is 0: it is for staff, not one player).</summary>
        [JsonPropertyName("currencyAlert")]
        public CurrencyAlertNotificationDto? CurrencyAlert { get; set; }

        /// <summary>Set when Type is LootboxWorldChanged (then UserId is 0: it is for the game server).</summary>
        [JsonPropertyName("lootboxWorldChanged")]
        public LootboxWorldChangedNotificationDto? LootboxWorldChanged { get; set; }

        /// <summary>Set when Type is DiscoveryReset.</summary>
        [JsonPropertyName("discoveryReset")]
        public DiscoveryResetNotificationDto? DiscoveryReset { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }

    public static class PlayerNotificationTypes
    {
        public const string TitleChanged = "TitleChanged";

        /// <summary>
        /// A user's group memberships changed (web app, API, or a temporary rank expiring) - the
        /// plugin re-reads the player so their rank shows in chat and the tab list straight away.
        /// No payload.
        /// </summary>
        public const string RankChanged = "RankChanged";

        /// <summary>
        /// Lootbox token items were issued to the player by the API itself (a premium tier or kit grant rule,
        /// docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 5) - the plugin fetches and hands over their undelivered
        /// tokens. No payload.
        /// </summary>
        public const string LootboxTokensIssued = "LootboxTokensIssued";

        /// <summary>
        /// Another player paid this user (/pay, currency Phase 3). Payload in Payment. Queued for
        /// every completed transfer, so an offline recipient hears about it on their next join
        /// (within the queue's 24 h window; the ledger keeps the payment either way).
        /// </summary>
        public const string PaymentReceived = "PaymentReceived";

        /// <summary>
        /// A currency anomaly alert (currency Phase 5, DESIGN.md §3.9) for every online staff
        /// member holding knk.admin.currency.alerts - not addressed to one player (UserId 0, no
        /// UUID). Payload in CurrencyAlert. The alert itself stays on the web alerts page and in
        /// /knk currency alerts until acknowledged.
        /// </summary>
        public const string CurrencyAlert = "CurrencyAlert";

        /// <summary>
        /// Lootbox changes made outside the game (web despawn, web area delete, token revoke) that the game server
        /// should apply now rather than at its next runtime refresh (smoke test 2026-09-27: a despawned box stayed
        /// clickable for up to a minute, a revoked token sat in the inventory). Not addressed to one player (UserId 0).
        /// Payload in LootboxWorldChanged.
        /// </summary>
        public const string LootboxWorldChanged = "LootboxWorldChanged";

        /// <summary>
        /// One of this user's discoveries was reset (web admin player profile or /knk discovery
        /// reset, domain discovery DESIGN.md §3.6). The plugin keeps a per-session known set loaded
        /// at join; on this it re-reads that set and re-checks the player's current location, so
        /// the reset place can be discovered again without a rejoin. Payload in DiscoveryReset.
        /// Harmless for an offline player: the known set is loaded fresh on join anyway.
        /// </summary>
        public const string DiscoveryReset = "DiscoveryReset";
    }

    /// <summary>Payload of a DiscoveryReset player notification.</summary>
    public class DiscoveryResetNotificationDto
    {
        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        /// <summary>The domain's WorldGuard region id, null when it has none.</summary>
        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }
    }

    /// <summary>What a LootboxWorldChanged notification carries.</summary>
    public class LootboxWorldChangedNotificationDto
    {
        // World boxes that are gone (status Removed): the plugin removes their entities.
        [JsonPropertyName("removedSpawnIds")]
        public List<int> RemovedSpawnIds { get; set; } = new();

        // Revoked token items: the plugin removes every copy from online players' inventories and ender chests.
        [JsonPropertyName("revokedTokens")]
        public List<Guid> RevokedTokens { get; set; } = new();
    }

    public class AcknowledgePlayerNotificationsDto
    {
        [JsonPropertyName("ids")]
        public List<long> Ids { get; set; } = new();
    }
}
