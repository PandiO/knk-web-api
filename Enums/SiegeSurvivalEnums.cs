namespace knkwebapi_v2.Enums;

// Siege Survival phase 1 enums (knk-workspace docs/specs/siege-survival/DESIGN.md §2 S01–S68, §7, §8, §8a;
// PandiO/knk-workspace#41). Stored as strings (HasConversion<string>()) and serialized by name by the global
// JsonStringEnumConverter. The plugin's knk-core survival records use the same names in upper snake case
// (OWN_INVENTORY, ENCHANTED_BOOKS, …); knk-api-client maps them.

/// <summary>DESIGN §8 / S11 / S23: the three equipment modes, all three in the first release.</summary>
public enum SurvivalEquipmentMode
{
    OwnInventory,
    Kits,
    MatchProgression
}

/// <summary>
/// DESIGN §3a / S46: a survival lobby's override of the scenario's "end when all objectives are captured"
/// setting, resolved lobby → scenario like the navigation compass (navigation-compass DESIGN C9).
/// </summary>
public enum SurvivalAllCapturedOverride
{
    Inherit,
    On,
    Off
}

/// <summary>DESIGN §7 / S29: the growth presets shared by the enemy-count curve and the stat curve.</summary>
public enum SurvivalScalingPreset
{
    Linear,
    Logarithmic,
    Exponential,
    Hybrid
}

/// <summary>DESIGN §6: how a spawnzone's objective prerequisites combine.</summary>
public enum SurvivalPrerequisitePolicy
{
    All,
    Any
}

/// <summary>DESIGN §4 "Objective and gate eligibility overrides": follow standard rules, explicitly allow, explicitly deny.</summary>
public enum SurvivalTargetOverrideMode
{
    Standard,
    Allow,
    Deny
}

/// <summary>DESIGN §7 / S14 / S53: a special-wave schedule entry recurs at an interval or names one wave.</summary>
public enum SurvivalSpecialWaveKind
{
    Recurring,
    Explicit
}

/// <summary>DESIGN §8a / S24: the two Mystery Box variants, same point type and roll path, different pool kind.</summary>
public enum SurvivalMysteryBoxVariant
{
    Items,
    EnchantedBooks
}

/// <summary>DESIGN §6: a spawnzone point is either a spawnpoint or one point of the optional ordered lane override.</summary>
public enum SurvivalSpawnZonePointKind
{
    Spawn,
    Lane
}

/// <summary>
/// DESIGN §3 / §3a / S47 / S61 / S62: the conceptual end reasons of a survival match (knk-core's
/// SurvivalEndReason). Result-contract only in phase 1; persisted by phase 5 (link S8).
/// </summary>
public enum SurvivalEndReason
{
    TeamWipe,
    InstaLoseObjectiveCaptured,
    AllObjectivesCaptured,
    NoPlayersLeft,
    AdminAbort,
    ServerRestart,
    TechnicalLimit
}

/// <summary>DESIGN §3: the survival substates inside a running match (design names, not SiegeMatchStatus).</summary>
public enum SurvivalSubstate
{
    Preparing,
    WaveActive,
    Intermission,
    Ending
}
