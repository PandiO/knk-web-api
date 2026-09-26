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
    // 12 is allocated to teleport (PlayerTeleported) on its own branch.
    // Lootboxes (docs/specs/lootboxes/DESIGN.md §3.2), numbers 13-14 allocated to this feature. Player claims are
    // logged in LootboxClaim, not here. The range has no room for separate area values, so LootboxSpawnedByAdmin covers
    // every staff change to where boxes spawn: Details.event is Spawned (/knk lootbox spawn), AreaCreated or
    // AreaDeleted (/knk lootbox area create|delete), recorded against the staff member.
    LootboxSpawnedByAdmin = 13,
    // A staff give (/knk lootbox give): target = the player who got the item.
    LootboxGranted = 14
}
