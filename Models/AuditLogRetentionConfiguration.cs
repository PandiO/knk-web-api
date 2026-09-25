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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
