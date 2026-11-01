namespace knkwebapi_v2.Models;

// One player's participation in a SiegeMatch (docs/specs/siege-minigame/DESIGN.md §3.10): stats
// and the rewards granted for it (§7.6 - these rows are the rewards' audit trail). History row, no
// [FormConfigurableEntity]. Cascades with its match; the User is Restrict; the team is SetNull so a
// played scenario's teams can still be edited/deleted without losing the history.
public class SiegeMatchParticipant
{
    public int Id { get; set; }

    public int SiegeMatchId { get; set; }
    public SiegeMatch SiegeMatch { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int? SiegeTeamId { get; set; }
    public SiegeTeam? SiegeTeam { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int HighestKillStreak { get; set; }
    public int Captures { get; set; }

    // Alpha run 1 (PandiO/knk-workspace#165/#166), reported by the plugin with "complete": the gate HP this member
    // took off the scenario's gates (whole HP), and their active (not AFK) seconds out of their time in the match
    // (first join to the end or their leave, rejoins included). Basis of the gate damage and participation rewards.
    public int GateDamage { get; set; }
    public int ActiveSeconds { get; set; }
    public int MatchSeconds { get; set; }

    public int CoinsAwarded { get; set; }
    public int ExpAwarded { get; set; }
    public int GemsAwarded { get; set; }
}
