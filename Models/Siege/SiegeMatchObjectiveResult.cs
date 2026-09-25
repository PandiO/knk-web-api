namespace knkwebapi_v2.Models;

// One capture of an objective in a SiegeMatch (docs/specs/siege-minigame/DESIGN.md §3.10, §7.3):
// with AllowRecapture each capture writes its own row; the final holder is what counts at the end.
// History row, no [FormConfigurableEntity]. Cascades with its match; objective/team refs are SetNull
// (editing a played scenario keeps the history), the capturing User is Restrict.
public class SiegeMatchObjectiveResult
{
    public int Id { get; set; }

    public int SiegeMatchId { get; set; }
    public SiegeMatch SiegeMatch { get; set; } = null!;

    public int? SiegeObjectiveId { get; set; }
    public SiegeObjective? SiegeObjective { get; set; }

    public int? FinalHolderTeamId { get; set; }
    public SiegeTeam? FinalHolderTeam { get; set; }

    public int? CapturedByUserId { get; set; }
    public User? CapturedByUser { get; set; }

    public DateTime? CapturedAt { get; set; }
}
