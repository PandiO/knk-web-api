using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Append-only record of an admin/system mutation affecting one player
/// (docs/specs/user-management/DESIGN.md §4). Not [FormConfigurableEntity] — this is
/// intentionally not on the generic FormWizard CRUD path (IMPLEMENTATION_PLAN.md Phase 2:
/// "append-only, viewed not edited"); read via GET /api/audit-log only.
///
/// ActorUserId/TargetUserId are plain int columns with no FK navigation to User on purpose: an
/// audit trail needs to keep referring to "user 42" even if that user is later deleted, and a
/// hard FK would either block the delete or force a cascade that erases the very history this
/// table exists to preserve.
/// </summary>
public class AuditLogEntry
{
    public int Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Who made the change. Null for system-initiated mutations (e.g. an automatic salary payout on player join).</summary>
    public int? ActorUserId { get; set; }

    /// <summary>Which player was affected.</summary>
    public int TargetUserId { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>Free-form JSON with before/after values, specific to Action. Nullable for actions that are self-describing.</summary>
    public string? Details { get; set; }
}
