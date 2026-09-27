using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Singleton configuration entity for how long AuditLogEntry rows (docs/specs/user-management/
/// DESIGN.md §4) are kept before RetentionPolicyService deletes them (§7 item 3 /
/// IMPLEMENTATION_PLAN.md §5 item 3). Mirrors SalaryConfiguration/GameSettings' singleton
/// pattern (fixed "global" Id).
/// </summary>
public class AuditLogRetentionConfiguration
{
    public string Id { get; set; } = "global";

    /// <summary>
    /// How many days an AuditLogEntry is kept before it's eligible for deletion. Default 180
    /// (~6 months) — a deliberately longer default than FormSubmissionProgress's 14-day
    /// precedent, since this is an audit/compliance-adjacent trail rather than transient
    /// in-progress form state. Tune via PUT.
    /// </summary>
    public int RetentionDays { get; set; } = 180;

    /// <summary>
    /// How many days a PrivateMessageLogEntry is kept (docs/specs/private-messages/DESIGN.md §3.1,
    /// developer decision 2026-09-26: 30). Much shorter than the audit trail on purpose - it is
    /// players' private message content, kept only for moderation.
    /// </summary>
    public int PrivateMessageRetentionDays { get; set; } = DefaultPrivateMessageRetentionDays;

    public const int DefaultPrivateMessageRetentionDays = 30;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
