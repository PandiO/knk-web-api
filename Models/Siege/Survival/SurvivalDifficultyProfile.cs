using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (DESIGN §7, §10 "Difficulty/wave profile", S21, S29, S30, S54, S55; PandiO/knk-workspace#41).
// A scenario can hold several profiles ("select scenario, equipment mode and difficulty", §3 step 2); the one with
// IsDefault is used when a match names none. Owned by the scenario (cascade).
//
// Curves (S29): round r starts at 1; linear b + a·(r−1), logarithmic b + a·ln(r), exponential b·m^(r−1), hybrid
// linear up to the switch round s then ×m per round. The count curve's base is the round-1 enemy count; the stat
// curve's base is always 1 (S30: round 1 = vanilla) and each stat follows it with its own weight w:
// multiplier = 1 + w·(curve(r) − 1) (S54).
[FormConfigurableEntity("SurvivalDifficultyProfile")]
public class SurvivalDifficultyProfile
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }

    // ---- Enemy count curve ----
    public SurvivalScalingPreset CountPreset { get; set; } = SurvivalScalingPreset.Linear;
    public double CountBase { get; set; } = 6;          // b: round-1 enemy count (≥ 1)
    public double CountStep { get; set; } = 2;          // a
    public double CountMultiplier { get; set; } = 1.1;  // m
    public int CountSwitchRound { get; set; } = 9;      // s (hybrid)

    // ---- Stat curve (b = 1) ----
    public SurvivalScalingPreset StatPreset { get; set; } = SurvivalScalingPreset.Linear;
    public double StatStep { get; set; } = 0.1;
    public double StatMultiplier { get; set; } = 1.1;
    public int StatSwitchRound { get; set; } = 9;

    // ---- Per-stat weights (S54), 0 = the stat stays vanilla ----
    public double HealthWeight { get; set; } = 1;
    public double WalkingSpeedWeight { get; set; } = 0.3;
    public double MeleeDamageWeight { get; set; } = 1;
    public double RangedDamageWeight { get; set; } = 1;
    public double AttackSpeedWeight { get; set; } = 0.5;

    // ---- Per-player scaling (S55): × (1 + p·(players − 1)); always on the count, optionally on the stats ----
    public double PerPlayerFactor { get; set; } = 0.25;
    public bool PerPlayerAppliesToStats { get; set; } = false;

    // ---- Caps: 0 = unlimited for the counts; the stat cap is the maximum stat multiplier (applied last) ----
    public int MaxAlive { get; set; } = 40;
    public double StatCap { get; set; } = 8;
    public int CountCap { get; set; } = 0;

    // ---- Other difficulty values (DESIGN §5, §7) ----
    // Repair work per second per repairer, in door-health units (§5: P(t+dt) = min(H, P + r·n·dt)).
    public double RepairRatePerSecond { get; set; } = 50;
    // Factor on the NPCs' gate damage and on their weight in the capture calculation (weight 1 = a player).
    public double GateDamageFactor { get; set; } = 1;
    public double CaptureContributionWeight { get; set; } = 1;
    // Spawn pacing: minimum milliseconds between two spawns of a round.
    public int SpawnIntervalMillis { get; set; } = 500;
    // Aggro radius and leash distance in blocks; null = the plugin's built-in values (no balance value is fixed here).
    public double? AggroRadius { get; set; }
    public double? LeashDistance { get; set; }
}
