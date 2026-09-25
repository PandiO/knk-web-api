namespace knkwebapi_v2.Models;

// Append-only claim-history log (docs/specs/kits/DESIGN.md §2.3) - one row per successful claim,
// including first-join grants. Viewed, not edited - same convention as AuditLogEntry, so this is
// deliberately NOT [FormConfigurableEntity]. Cooldown check: the most recent ClaimedAt for
// (KitId, UserId) plus Kit.CooldownSeconds must be in the past.
public class KitClaim
{
    public int Id { get; set; }

    public int KitId { get; set; }
    public Kit Kit { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime ClaimedAt { get; set; } = DateTime.UtcNow;
}
