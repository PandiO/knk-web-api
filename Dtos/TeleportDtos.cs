using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// Body for POST /api/users/{id}/teleport-audit (docs/specs/teleport/DESIGN.md §3.10): one
    /// staff teleport made in-game, recorded as a PlayerTeleported audit entry on user {id} - the
    /// visited player for "/tp &lt;player&gt;", otherwise the moved player. The acting staff member
    /// is not part of the body: like every other plugin write, it comes from the X-Acting-User-Id
    /// header (honoured only with the plugin key when Security:PluginApiKey is set).
    /// </summary>
    public class TeleportAuditDto
    {
        /// <summary>The plugin's TeleportKind name, e.g. "STAFF" (see UserService.TeleportAuditKinds).</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = null!;

        /// <summary>The player who moved.</summary>
        [JsonPropertyName("subjectUserId")]
        public int SubjectUserId { get; set; }

        /// <summary>The player whose location was the destination, when there was one.</summary>
        [JsonPropertyName("visitedUserId")]
        public int? VisitedUserId { get; set; }

        [JsonPropertyName("from")]
        public TeleportAuditPointDto From { get; set; } = null!;

        [JsonPropertyName("to")]
        public TeleportAuditPointDto To { get; set; } = null!;

        /// <summary>The destination domain (warps, Phase 5); null for player/coordinate teleports.</summary>
        [JsonPropertyName("domainId")]
        public int? DomainId { get; set; }

        /// <summary>Whether the moved/visited player was left un-notified (-s, or a vanished staff member).</summary>
        [JsonPropertyName("silent")]
        public bool Silent { get; set; }

        /// <summary>Optional free-text reason given by the staff member.</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        /// <summary>How it was started: "command" (a staff member in game) or "console". Defaults to "command".</summary>
        [JsonPropertyName("via")]
        public string? Via { get; set; }
    }

    public class TeleportAuditPointDto
    {
        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }
    }

    // ===== Warp destinations (docs/specs/teleport/DESIGN.md §3.7, KNG-17 Phase 5) =====

    /// <summary>Where a warp lands: the domain's Location.</summary>
    public class TeleportLocationDto
    {
        [JsonPropertyName("world")]
        public string? World { get; set; }

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }

        [JsonPropertyName("yaw")]
        public float Yaw { get; set; }

        [JsonPropertyName("pitch")]
        public float Pitch { get; set; }
    }

    /// <summary>
    /// One warp destination as one player sees it (GET /api/teleport-destinations?userId=).
    /// Requirements are evaluated in DESIGN §3.7.2's order - title, premium tier, discovery, price -
    /// and the first one that fails is the lock. The plugin applies its bypass nodes on top:
    /// knk.teleport.bypass.requirements ignores the requirement locks (requirementsMet),
    /// knk.teleport.bypass.cost the price (canAfford).
    /// </summary>
    public class TeleportDestinationDto
    {
        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        /// <summary>Town, District, Structure or GateStructure.</summary>
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = null!;

        [JsonPropertyName("location")]
        public TeleportLocationDto Location { get; set; } = null!;

        [JsonPropertyName("priceGems")]
        public int PriceGems { get; set; }

        /// <summary>The required title in the player's gender, when there is one.</summary>
        [JsonPropertyName("minTitleName")]
        public string? MinTitleName { get; set; }

        [JsonPropertyName("minPremiumTierName")]
        public string? MinPremiumTierName { get; set; }

        [JsonPropertyName("requiresDiscovery")]
        public bool RequiresDiscovery { get; set; }

        /// <summary>No lock at all: the player can warp here now.</summary>
        [JsonPropertyName("available")]
        public bool Available { get; set; }

        /// <summary>Title, premium tier and discovery requirements all met.</summary>
        [JsonPropertyName("requirementsMet")]
        public bool RequirementsMet { get; set; }

        /// <summary>The player has at least priceGems gems.</summary>
        [JsonPropertyName("canAfford")]
        public bool CanAfford { get; set; }

        /// <summary>The first failed check: TitleTooLow, PremiumTooLow, NotDiscovered or InsufficientGems; null when available.</summary>
        [JsonPropertyName("lockCode")]
        public string? LockCode { get; set; }

        /// <summary>Player-facing text for lockCode, e.g. "Reach title Knight to unlock".</summary>
        [JsonPropertyName("lockReason")]
        public string? LockReason { get; set; }
    }

    /// <summary>
    /// POST /api/teleport-destinations/{domainId}/charge - sent by the plugin after the warmup.
    /// Re-evaluates the destination for the player and charges its price (ledger TELEPORT_FEE).
    /// The idempotency key is the plugin's, one per warp attempt: a retry with the same key
    /// returns the first charge instead of charging again.
    /// </summary>
    public class TeleportChargeRequestDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        /// <summary>1-100 characters of A-Z a-z 0-9 : _ . - (e.g. "warp:&lt;uuid&gt;").</summary>
        [JsonPropertyName("idempotencyKey")]
        public string IdempotencyKey { get; set; } = null!;

        /// <summary>The player holds knk.teleport.bypass.requirements (checked by the plugin).</summary>
        [JsonPropertyName("bypassRequirements")]
        public bool BypassRequirements { get; set; }

        /// <summary>The player holds knk.teleport.bypass.cost: nothing is charged.</summary>
        [JsonPropertyName("bypassCost")]
        public bool BypassCost { get; set; }
    }

    /// <summary>
    /// POST /api/teleport-destinations/request-fee - a paid /tpa or /tpahere (teleport.request.price-coins
    /// in the plugin's config), charged to the requester when the teleport commits (DESIGN §3.5).
    /// </summary>
    public class TeleportRequestFeeDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("amountCoins")]
        public int AmountCoins { get; set; }

        [JsonPropertyName("idempotencyKey")]
        public string IdempotencyKey { get; set; } = null!;

        /// <summary>The other player of the request, for the ledger's metadata.</summary>
        [JsonPropertyName("otherUserId")]
        public int? OtherUserId { get; set; }
    }

    /// <summary>
    /// POST /api/teleport-destinations/back-fee - the flat coin fee of a player's own /back (the
    /// plugin's teleport.back.price-coins), charged when the teleport commits (Linear KNG-42).
    /// </summary>
    public class TeleportBackFeeDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("amountCoins")]
        public int AmountCoins { get; set; }

        [JsonPropertyName("idempotencyKey")]
        public string IdempotencyKey { get; set; } = null!;

        /// <summary>What the player goes back to (death, warps, teleport, spawn), for the ledger's source reference.</summary>
        [JsonPropertyName("backKind")]
        public string? BackKind { get; set; }
    }

    /// <summary>A charged (or free) teleport. For a warp it carries the authoritative destination.</summary>
    public class TeleportChargeResultDto
    {
        /// <summary>"Gems" for warps, "Coins" for requests and /back.</summary>
        [JsonPropertyName("currency")]
        public string Currency { get; set; } = null!;

        /// <summary>What this charge took; 0 for a free warp or with bypassCost.</summary>
        [JsonPropertyName("charged")]
        public long Charged { get; set; }

        /// <summary>The player's balance of that currency after the charge.</summary>
        [JsonPropertyName("newBalance")]
        public long NewBalance { get; set; }

        /// <summary>True when this key had already been charged: nothing new was taken.</summary>
        [JsonPropertyName("replayed")]
        public bool Replayed { get; set; }

        /// <summary>The ledger transaction's public id; null when nothing was charged.</summary>
        [JsonPropertyName("transactionPublicId")]
        public string? TransactionPublicId { get; set; }

        [JsonPropertyName("destination")]
        public TeleportDestinationDto? Destination { get; set; }
    }

    /// <summary>POST /api/teleport-destinations/refund - the teleport a charge paid for didn't happen.</summary>
    public class TeleportRefundRequestDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        /// <summary>The key the charge was made with.</summary>
        [JsonPropertyName("idempotencyKey")]
        public string IdempotencyKey { get; set; } = null!;

        /// <summary>Why, for the ledger (e.g. "teleport cancelled by a listener"). Optional.</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    public class TeleportRefundResultDto
    {
        /// <summary>False when nothing had been charged under the key; the key is then void, so a
        /// charge that arrives late with it is refused instead of taking the player's money.</summary>
        [JsonPropertyName("refunded")]
        public bool Refunded { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("newBalance")]
        public long? NewBalance { get; set; }

        /// <summary>True when the charge had already been refunded (by an earlier call or by staff).</summary>
        [JsonPropertyName("replayed")]
        public bool Replayed { get; set; }
    }
}
