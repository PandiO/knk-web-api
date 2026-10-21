namespace knkwebapi_v2.Models;

/// <summary>
/// A staff bar on value-moving duels (coin or item stakes, drop mode) for one player, or for one pair when
/// <see cref="OtherUserId"/> is set (arena duels DESIGN.md §10.3, §13.3 P1; KNG-213). Friendly duels stay
/// allowed. Active from creation until <see cref="ExpiresAt"/> (null = until lifted) or <see cref="LiftedAt"/>.
/// Rows are kept after they end; creating and lifting are audited.
/// </summary>
public class DuelRestriction
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Set for a pair restriction (either order).</summary>
    public int? OtherUserId { get; set; }
    public User? OtherUser { get; set; }

    public string Reason { get; set; } = null!;

    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }

    public DateTime? LiftedAt { get; set; }
    public int? LiftedByUserId { get; set; }
    public string? LiftReason { get; set; }

    public bool IsActive(DateTime now) => LiftedAt == null && (ExpiresAt == null || ExpiresAt > now);

    public bool Covers(int a, int b) =>
        OtherUserId == null
            ? UserId == a || UserId == b
            : (UserId == a && OtherUserId == b) || (UserId == b && OtherUserId == a);
}
