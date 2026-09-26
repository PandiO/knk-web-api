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
    PlayerTeleported = 12
}
