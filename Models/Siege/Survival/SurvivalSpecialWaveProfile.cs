using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (DESIGN §7 "Special profiles", S14, S53; PandiO/knk-workspace#41): a special-wave enemy
// profile of a scenario, referenced by the schedule entries (SurvivalSpecialWaveEntry). It replaces the normal
// composition unless Additive is on (boss events on top of the normal wave). Owned by the scenario (cascade);
// its own monster types cascade with it. An empty mix means "the scenario's normal mix".
[FormConfigurableEntity("SurvivalSpecialWaveProfile")]
public class SurvivalSpecialWaveProfile
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    // The profile id the plugin's schedule uses; unique per scenario, stored lower case ("wolves").
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }

    // Null = derive the count from the normal count × CountMultiplier.
    public int? CountOverride { get; set; }
    public double CountMultiplier { get; set; } = 1;
    public bool Additive { get; set; } = false;

    [RelatedEntityField(typeof(SurvivalSpecialWaveMonsterType))]
    public ICollection<SurvivalSpecialWaveMonsterType> MonsterTypes { get; set; } = new List<SurvivalSpecialWaveMonsterType>();
}

// A monster type of a special-wave profile (same shape as SurvivalMonsterType). Cascades with its profile.
[FormConfigurableEntity("SurvivalSpecialWaveMonsterType")]
public class SurvivalSpecialWaveMonsterType
{
    public int Id { get; set; }

    [NavigationPair(nameof(SurvivalSpecialWaveProfile))]
    [RelatedEntityField(typeof(SurvivalSpecialWaveProfile))]
    public int SurvivalSpecialWaveProfileId { get; set; }
    [RelatedEntityField(typeof(SurvivalSpecialWaveProfile))]
    public SurvivalSpecialWaveProfile SurvivalSpecialWaveProfile { get; set; } = null!;

    public string EntityType { get; set; } = null!;
    public double Weight { get; set; } = 1;
    public int UnlockRound { get; set; } = 1;
    public int SortOrder { get; set; }
}
