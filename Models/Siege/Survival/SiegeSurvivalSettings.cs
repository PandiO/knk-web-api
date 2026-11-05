using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (knk-workspace docs/specs/siege-survival/DESIGN.md §10 "Survival scenario settings";
// PandiO/knk-workspace#41). One row per scenario that plays as survival, 1:1 with SiegeScenario (unique
// SiegeScenarioId). A scenario without a row is an ordinary PvP siege and nothing survival-related applies to it:
// its readiness, runtime-config and match flow are untouched. Kept as its own entity instead of columns on
// SiegeScenario so that the PvP scenario model (and the branches editing it) stay as they are.
//
// Owned survival children (difficulty profiles, monster types, special waves, spawnzones, target overrides,
// purchase points, Mystery Box pools and points) hang off the scenario itself, so a settings row can be deleted
// and recreated without losing them; all of them cascade with the scenario.
[FormConfigurableEntity("SiegeSurvivalSettings")]
public class SiegeSurvivalSettings
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    // Off = the scenario keeps its survival authoring but plays (and is listed) as ordinary PvP.
    public bool Enabled { get; set; } = true;

    // S37: a single player can start a match. The scenario's own PlayersMin keeps its PvP meaning.
    public int MinPlayers { get; set; } = 1;

    // The human defender team and the NPC attacker team (DESIGN §1, §10 "human/NPC team references"). Null =
    // the scenario's first Defender / first Attacker team, resolved in the frozen configuration. SetNull when
    // the team is deleted, so the default applies again.
    [NavigationPair(nameof(HumanTeam))]
    [RelatedEntityField(typeof(SiegeTeam))]
    public int? HumanTeamId { get; set; }
    [RelatedEntityField(typeof(SiegeTeam))]
    public SiegeTeam? HumanTeam { get; set; }

    [NavigationPair(nameof(NpcTeam))]
    [RelatedEntityField(typeof(SiegeTeam))]
    public int? NpcTeamId { get; set; }
    [RelatedEntityField(typeof(SiegeTeam))]
    public SiegeTeam? NpcTeam { get; set; }

    // ---- Equipment (DESIGN §8, S23, S38) ----
    public SurvivalEquipmentMode EquipmentMode { get; set; } = SurvivalEquipmentMode.OwnInventory;

    // Kits mode: the kits a player may pick (join rows) and the one a player who doesn't pick gets (S38).
    [RelatedEntityField(typeof(SurvivalSettingsKit))]
    public ICollection<SurvivalSettingsKit> SelectedKits { get; set; } = new List<SurvivalSettingsKit>();

    [NavigationPair(nameof(DefaultKit))]
    [RelatedEntityField(typeof(Kit))]
    public int? DefaultKitId { get; set; }
    [RelatedEntityField(typeof(Kit))]
    public Kit? DefaultKit { get; set; }

    // Match progression: the baseline loadout granted at entry, authored as a Kit; null = an empty inventory.
    [NavigationPair(nameof(BaselineLoadoutKit))]
    [RelatedEntityField(typeof(Kit))]
    public int? BaselineLoadoutKitId { get; set; }
    [RelatedEntityField(typeof(Kit))]
    public Kit? BaselineLoadoutKit { get; set; }

    // ---- Lifecycle (S01, S27) ----
    public int IntermissionMillis { get; set; } = 10_000;

    // ---- End conditions (DESIGN §3a, S19, S26, S46). The per-objective insta-lose flag is SiegeObjective.InstantVictory. ----
    public bool EndWhenAllObjectivesCaptured { get; set; } = false;

    // ---- Spawn selection (DESIGN §6, S50): the share of spawns that go to the NearZoneCount active zones nearest
    // the living players, 0–100 %; 0 = zone weight only. ----
    public int FavorNearPlayersPercent { get; set; } = 0;
    public int NearZoneCount { get; set; } = 1;

    // ---- Stuck enemies (DESIGN §3c, S43) ----
    public int StuckAfterMillis { get; set; } = 30_000;
    public int LastEnemiesCount { get; set; } = 3;
    public int RespawnLimit { get; set; } = 2;
    // Null = no round backstop.
    public int? RoundBackstopMillis { get; set; } = 300_000;
    public double MeaningfulDistance { get; set; } = 1.0;

    // ---- Per-kill rewards (DESIGN §9, S32, S33). Null coins/XP = the scenario's CoinRewardCapture / ExpRewardCapture ÷ 10. ----
    public decimal? CoinsPerKill { get; set; }
    public decimal? XpPerKill { get; set; }
    public int MatchCurrencyPerKill { get; set; } = 70;
    public decimal KillBonusMultiplier { get; set; } = 1.1m;
}

// Kits mode (S23): a kit an admin selected for the scenario. Join row, cascades with the settings; the Kit is Restrict.
[FormConfigurableEntity("SurvivalSettingsKit")]
public class SurvivalSettingsKit
{
    [NavigationPair(nameof(SiegeSurvivalSettings))]
    [RelatedEntityField(typeof(SiegeSurvivalSettings))]
    public int SiegeSurvivalSettingsId { get; set; }
    [RelatedEntityField(typeof(SiegeSurvivalSettings))]
    public SiegeSurvivalSettings SiegeSurvivalSettings { get; set; } = null!;

    [NavigationPair(nameof(Kit))]
    [RelatedEntityField(typeof(Kit))]
    public int KitId { get; set; }
    [RelatedEntityField(typeof(Kit))]
    public Kit Kit { get; set; } = null!;

    public int SortOrder { get; set; }
}
