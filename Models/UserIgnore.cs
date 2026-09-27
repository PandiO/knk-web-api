namespace knkwebapi_v2.Models;

/// <summary>
/// One entry on a player's ignore list (KNG-18 Phase 2, docs/specs/private-messages/DESIGN.md
/// §3.1): <see cref="UserId"/> doesn't receive <see cref="IgnoredUserId"/>'s private messages and
/// doesn't see their public chat. Unique per (UserId, IgnoredUserId); both sides cascade on user
/// delete. Managed only through <c>api/users/{id}/ignores</c> (UserIgnoresController) - no
/// [FormConfigurableEntity].
/// </summary>
public class UserIgnore
{
    public int Id { get; set; }

    /// <summary>The player doing the ignoring.</summary>
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int IgnoredUserId { get; set; }
    public User IgnoredUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
