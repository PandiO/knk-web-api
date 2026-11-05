using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (DESIGN §7, §10 "Special-wave schedule", S14, S53; PandiO/knk-workspace#41): one entry of
// the special-wave schedule. Recurring: every Interval waves from StartWave on (5, 10, 15 for interval 5 and
// start 5); Explicit: one wave number. An explicit entry beats every recurring entry for its wave; conflicting
// recurring entries need different priorities (the lowest value wins) or readiness fails. Owned by the scenario
// (cascade); the profile it names must belong to the same scenario and cascades too.
[FormConfigurableEntity("SurvivalSpecialWaveEntry")]
public class SurvivalSpecialWaveEntry
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    [NavigationPair(nameof(SurvivalSpecialWaveProfile))]
    [RelatedEntityField(typeof(SurvivalSpecialWaveProfile))]
    public int SurvivalSpecialWaveProfileId { get; set; }
    [RelatedEntityField(typeof(SurvivalSpecialWaveProfile))]
    public SurvivalSpecialWaveProfile SurvivalSpecialWaveProfile { get; set; } = null!;

    public SurvivalSpecialWaveKind Kind { get; set; } = SurvivalSpecialWaveKind.Recurring;

    // Recurring
    public int? Interval { get; set; }
    public int? StartWave { get; set; }
    public int Priority { get; set; } = 1;

    // Explicit
    public int? Wave { get; set; }

    public int SortOrder { get; set; }
}
