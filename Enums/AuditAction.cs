namespace knkwebapi_v2.Enums;

/// <summary>
/// The kind of mutation an <see cref="knkwebapi_v2.Models.AuditLogEntry"/> records
/// (docs/specs/user-management/DESIGN.md §4). Serialized as its PascalCase string name
/// (System.Text.Json's default for this codebase — see ActiveMode/GatePassThroughMethod's own
/// doc comments for why the frontend must not assume numeric wire values).
/// </summary>
public enum AuditAction
{
    GroupAssigned = 0,
    GroupRemoved = 1,
    GrantAdded = 2,
    GrantUpdated = 3,
    GrantRemoved = 4,
    TitleChanged = 5,
    VanishToggled = 6,
    SalaryPayout = 7,
    BalanceAdjusted = 8,
    PlayerFrozen = 9,
    PlayerUnfrozen = 10,
    KitGranted = 11,
    // A staff /tp, /tphere (and later /spawn <p>, /warp <d> <p>) made in game - recorded by the
    // plugin through POST /api/users/{id}/teleport-audit (docs/specs/teleport/DESIGN.md §3.10).
    PlayerTeleported = 12,
    // Lootboxes (docs/specs/lootboxes/DESIGN.md §3.2), numbers 13-14 allocated to this feature. Player claims are
    // logged in LootboxClaim, not here. The range has no room for separate area values, so LootboxSpawnedByAdmin covers
    // every staff change to where boxes spawn: Details.event is Spawned (/knk lootbox spawn), AreaCreated or
    // AreaDeleted (/knk lootbox area create|delete), recorded against the staff member.
    LootboxSpawnedByAdmin = 13,
    // A staff give (/knk lootbox give): target = the player who got the item.
    LootboxGranted = 14,

    /// <summary>
    /// A staff member (or the game server) read a player's private messages
    /// (docs/specs/private-messages/DESIGN.md §3.2) - target = the player whose messages were read.
    /// </summary>
    PrivateMessagesViewed = 15,

    /// <summary>Staff reset one domain discovery so it can be discovered (and rewarded) again
    /// (docs/specs/domain-discovery/DESIGN.md §3.5). Grants themselves are DISCOVERY_REWARD
    /// ledger postings (plus a TitleChanged row when their XP crosses a title bracket).</summary>
    DiscoveryReset = 17,

    // Currency payments (docs/specs/currency-payments/IMPLEMENTATION_PLAN.md Phase 4); values
    // 19–29 are reserved for this feature so parallel branches don't collide.
    CurrencyPolicyChanged = 19,
    CurrencyTransactionReversed = 20,
    CurrencyTransferLocked = 21,
    CurrencyTransferUnlocked = 22,

    // Player statistics, diagnostics and privacy (KNG-34, knk-workspace docs/specs/player-statistics/
    // IMPLEMENTATION_PLAN.md §8 link 6); values 30–34 are reserved for this feature.

    /// <summary>The owner read diagnostic events (timeline, search or one event). Target = the player
    /// whose events were read, or the reader when the search named no player.</summary>
    TelemetryViewed = 30,

    /// <summary>A GDPR deletion request was filed for the target player (by the player, staff or the
    /// owner; Details.source).</summary>
    PrivacyDeletionRequested = 31,

    /// <summary>A GDPR deletion request was executed (by the owner, or by the scheduled job when the
    /// actor is null). Details hold only counts.</summary>
    PrivacyDeletionExecuted = 32,

    /// <summary>An open GDPR deletion request was cancelled (by the player, staff or the owner).</summary>
    PrivacyDeletionCancelled = 33,

    /// <summary>The player confirmed their deletion request through the emailed link; it runs after
    /// the grace period.</summary>
    PrivacyDeletionConfirmed = 34,

    /// <summary>
    /// Closed-alpha hardening WP5.3: the game server created the Minecraft account for a name a
    /// web-only account held, so that account was renamed to unclaimed-&lt;id&gt; (target = the
    /// renamed web-only account). Values 35-36 are allocated to the alpha hardening
    /// (30-34 belong to player statistics).
    /// </summary>
    UsernameReleased = 35,

    // Invitations, Minecraft linking and admission (knk-workspace docs/specs/invitations/DESIGN.md § 6,
    // KNG-212/KNG-214); values 37-42 are allocated to this feature (36 stays unused).

    /// <summary>Staff created an invitation. Target = the actor (no account exists yet); Details name
    /// the invitation, never its code.</summary>
    InvitationCreated = 37,

    /// <summary>Staff revoked an invitation. Target = the actor.</summary>
    InvitationRevoked = 38,

    /// <summary>An account was created with an invitation. Target and actor = the new account.</summary>
    InvitationRedeemed = 39,

    /// <summary>A web account was linked to a Minecraft account (KNG-214).</summary>
    MinecraftLinked = 40,

    /// <summary>Staff unlinked a Minecraft account from a web login (KNG-214).</summary>
    MinecraftUnlinked = 41,

    /// <summary>Staff admitted a player without an invitation (KNG-214).</summary>
    PlayerAdmitted = 42,

    // Arena duels staff tools (KNG-213, knk-workspace docs/specs/arena-duels/DESIGN.md §10.3); values
    // 50–59 are reserved for arena duels. Target = the match's winner (review actions), the restricted
    // player (restrictions), or the staff member (policy). Details carry the match id and reason.

    /// <summary>A held duel result was paid out by staff.</summary>
    DuelResultReleased = 50,

    /// <summary>A held duel result was voided by staff: both stakes refunded, no fee.</summary>
    DuelResultVoided = 51,

    /// <summary>A paid duel result was reverted: the payout reversed (as far as the coins are still there), the loser refunded first.</summary>
    DuelResultReverted = 52,

    /// <summary>A paid duel result was confiscated: the payout reversed into SYS_CONFISCATED.</summary>
    DuelResultConfiscated = 53,

    /// <summary>A stakes-only duel restriction was placed on the target player (or a pair including them).</summary>
    DuelRestrictionCreated = 54,

    /// <summary>A duel restriction was lifted.</summary>
    DuelRestrictionLifted = 55,

    /// <summary>The review escalation of a pair or beneficiary was cleared (their open flags acknowledged).</summary>
    DuelEscalationCleared = 56,

    /// <summary>The duel review policy (DuelPolicy) changed. Target = the staff member.</summary>
    DuelPolicyChanged = 57
}
