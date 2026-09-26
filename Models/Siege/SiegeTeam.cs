using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// A team of a SiegeScenario (docs/specs/siege-minigame/DESIGN.md §3.4). Owned by its scenario
// (cascade); Spawnpoints are in turn owned by the team - two-level owned nesting, created through
// POST /api/SiegeScenarios/{id}/teams and POST /api/SiegeTeams/{id}/spawnpoints.
//
// Identity is Clan-sourced or ad-hoc (vision §7.3): each of name/colour/banner is the team's own
// value when set, else the Clan's. With no Clan all three are required (§3.9 readiness rule).
[FormConfigurableEntity("SiegeTeam")]
public class SiegeTeam
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    public int SortOrder { get; set; }

    public SiegeTeamRole Role { get; set; } = SiegeTeamRole.Attacker;

    // Teams sharing a value are allies, all others enemies. Replaces v2's siege_team_allies/
    // siege_team_enemies M2M tables - symmetric by construction.
    public int AllianceGroup { get; set; } = 1;

    // Restrict: a Clan used by a team can't be deleted out from under it.
    [NavigationPair(nameof(Clan))]
    [RelatedEntityField(typeof(Clan))]
    public int? ClanId { get; set; }
    [RelatedEntityField(typeof(Clan))]
    public Clan? Clan { get; set; }

    // Ad-hoc identity, or an override of the Clan's.
    public string? Name { get; set; }
    public string? ChatColor { get; set; }   // Bukkit ChatColor name, same set as Clan.ChatColor

    [NavigationPair(nameof(BannerDesign))]
    [RelatedEntityField(typeof(BannerDesign))]
    public int? BannerDesignId { get; set; }
    [RelatedEntityField(typeof(BannerDesign))]
    public BannerDesign? BannerDesign { get; set; }

    // v2 per-team action-bar message at match start.
    public string? StartMessage { get; set; }

    [RelatedEntityField(typeof(SiegeSpawnpoint))]
    public ICollection<SiegeSpawnpoint> Spawnpoints { get; set; } = new List<SiegeSpawnpoint>();
}
