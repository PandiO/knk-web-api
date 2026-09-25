using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// A siege map/mode (docs/specs/siege-minigame/DESIGN.md §3.3). Authored in several saves - owned
// children (Teams, Objectives) can only be created once the scenario has an id - so a saved
// scenario can be incomplete. Readiness (§3.9) is computed by SiegeScenarioService, never stored.
//
// Teams and Objectives are owned child collections with their own endpoints
// (POST /api/SiegeScenarios/{id}/teams|objectives); the scenario's own create/update ignores them.
// Districts and Gates are M2M joins edited as part of the scenario payload (replace-set).
[FormConfigurableEntity("SiegeScenario")]
public class SiegeScenario
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    // Restrict: shared world row.
    [NavigationPair(nameof(Town))]
    [RelatedEntityField(typeof(Town))]
    public int TownId { get; set; }
    [RelatedEntityField(typeof(Town))]
    public Town Town { get; set; } = null!;

    // The scenario area (lockdown scope, §8.5). Every district must belong to TownId (§3.9).
    [RelatedEntityField(typeof(SiegeScenarioDistrict))]
    public ICollection<SiegeScenarioDistrict> Districts { get; set; } = new List<SiegeScenarioDistrict>();

    // World-bound (captured in-game via the LocationSelection WorldTask). Restrict: a Location row
    // survives the scenario's deletion.
    [NavigationPair(nameof(HubLocation))]
    [RelatedEntityField(typeof(Location))]
    public int HubLocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location HubLocation { get; set; } = null!;

    public int PlayersMin { get; set; } = 2;
    public int PlayersMax { get; set; } = 50;

    // v1 entryTitle (Kits precedent). Restrict.
    [NavigationPair(nameof(MinTitleBracket))]
    [RelatedEntityField(typeof(TitleBracket))]
    public int? MinTitleBracketId { get; set; }
    [RelatedEntityField(typeof(TitleBracket))]
    public TitleBracket? MinTitleBracket { get; set; }

    // Match length (v2 formula as defaults): clamp(ceil(members × PerPlayer), Min, Max).
    public int MatchDurationMinSeconds { get; set; } = 300;
    public int MatchDurationPerPlayerSeconds { get; set; } = 75;
    public int MatchDurationMaxSeconds { get; set; } = 1800;

    // Rewards (v2 SiegeScenario/MGScenario fields, §7.6).
    public int CoinRewardWin { get; set; } = 100;
    public int ExpRewardWin { get; set; } = 10;
    public int GemRewardWin { get; set; } = 1;
    public int CoinRewardHolding { get; set; } = 50;
    public int ExpRewardHolding { get; set; } = 5;
    public int CoinRewardCapture { get; set; } = 50;
    public int ExpRewardCapture { get; set; } = 5;

    public bool LockdownScenarioArea { get; set; } = true;   // §8.5, D6
    public bool AllowRecapture { get; set; } = false;        // §7.3, D5
    public bool EnchantDropsEnabled { get; set; } = true;    // §9.4, D7

    // Owned children - cascade from the scenario.
    [RelatedEntityField(typeof(SiegeTeam))]
    public ICollection<SiegeTeam> Teams { get; set; } = new List<SiegeTeam>();

    [RelatedEntityField(typeof(SiegeObjective))]
    public ICollection<SiegeObjective> Objectives { get; set; } = new List<SiegeObjective>();

    // D3 admin-selected gates: M2M join with fields; only the join rows cascade.
    [RelatedEntityField(typeof(SiegeScenarioGate))]
    public ICollection<SiegeScenarioGate> Gates { get; set; } = new List<SiegeScenarioGate>();
}
