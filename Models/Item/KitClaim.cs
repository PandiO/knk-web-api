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

    /// <summary>
    /// Set when the API owes the player an in-game hand-over of this claim's items
    /// (PandiO/knk-workspace#261): a staff grant made outside the game server, e.g. "Grant" on the
    /// web app's player profile. Null for every claim the game server placed itself (/kit get|give,
    /// the kits menu, first-join kits) and for rows from before #261.
    /// </summary>
    public DateTime? DeliveryRequestedAt { get; set; }

    /// <summary>
    /// When a game server took the delivery (KitService.MarkDeliveredAsync). Set once, by a
    /// conditional update, so a grant is handed over at most once even when a push, a poll and a
    /// join race each other or two servers see it. Pending = DeliveryRequestedAt set and this null.
    /// </summary>
    public DateTime? DeliveredAt { get; set; }
}
