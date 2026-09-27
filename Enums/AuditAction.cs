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

    // 12-14 belong to other features' branches (teleport, lootboxes) - cross-branch allocation.

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
    CurrencyTransferUnlocked = 22
}
