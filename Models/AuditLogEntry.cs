using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Append-only record of an admin/system mutation affecting one player, or one other subject such as a permission
/// group (docs/specs/user-management/DESIGN.md §4; generic subject since PandiO/knk-workspace#50). Not [FormConfigurableEntity] — this is
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

    /// <summary>Which player was affected. Null when the entry is about a subject that is not one player (see
    /// <see cref="SubjectType"/>), e.g. a change to a permission group's own grants.</summary>
    public int? TargetUserId { get; set; }

    /// <summary>
    /// What the entry is about when that is not one player: a <see cref="AuditSubjectTypes"/> name such as
    /// <c>PermissionGroup</c>, with <see cref="SubjectId"/> its id (PandiO/knk-workspace#50, developer decision
    /// 2026-11-15). Generic on purpose: later group-like entities (clans that run towns, their treasuries) reuse the
    /// same pair instead of a column each. Null on player entries. A plain string, no FK, for the same reason as
    /// the user ids above: the trail outlives the subject.
    /// </summary>
    public string? SubjectType { get; set; }

    /// <summary>The id of the <see cref="SubjectType"/> record; null when SubjectType is null.</summary>
    public int? SubjectId { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>Free-form JSON with before/after values, specific to Action. Nullable for actions that are self-describing.</summary>
    public string? Details { get; set; }
}
