using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Global siege tunables (docs/specs/siege-minigame/DESIGN.md §3.8): every legacy literal the
/// runtime used, as an admin-editable value. Singleton with a fixed "global" Id - same pattern as
/// SalaryConfiguration/AuditLogRetentionConfiguration (created with these defaults on first read,
/// edited through GET/PUT /api/SiegeConfiguration). Not a [FormConfigurableEntity]: like
/// SalaryConfiguration it is a raw singleton, not a FormWizard list entity.
///
/// List-valued settings are stored as comma-separated strings and exposed as arrays by
/// SiegeConfigurationDto.
/// </summary>
public class SiegeConfiguration
{
    public const string SingletonId = "global";

    public string Id { get; set; } = SingletonId;

    // ---- Capture step (§7.2): attack = A1 + (a−1)·A2, defend = D1 + (d−1)·D2 ----
    public int CaptureAttackBase { get; set; } = 5;                          // A1
    public int CaptureAttackPerExtra { get; set; } = 2;                      // A2
    public int CaptureAttackPerExtraInstantVictory { get; set; } = 5;        // A2 on IV objectives
    public int CaptureDefendBase { get; set; } = 6;                          // D1
    public int CaptureDefendPerExtra { get; set; } = 3;                      // D2
    public int CaptureDefendPerExtraInstantVictory { get; set; } = 6;        // D2 on IV objectives

    // §7.4: capturing a side objective takes floor(IV.CapturePoints × this / sideCount) off every
    // uncaptured instant-victory objective (v1/v2 orig/5*2).
    public double SideCaptureReduction { get; set; } = 0.4;

    // ---- Matchmaking timeline (§6.2–6.4), seconds before match start ----
    public int VoteCloseSecondsBeforeStart { get; set; } = 30;
    public int DrawSecondsBeforeStart { get; set; } = 25;
    public int HubSecondsBeforeStart { get; set; } = 15;
    public int TeamSplitSecondsBeforeStart { get; set; } = 10;

    // Seconds-remaining marks at which matchmaking is announced.
    public string MatchmakingAnnouncementMarks { get; set; } = "290,60,30,15";

    // ---- Combat (§6.6–6.7) ----
    // Kill counts announced to the killer's team, and the streak above which streaks are announced.
    public string KillAnnouncementThresholds { get; set; } = "5,10,15";
    public int KillStreakAnnounceAbove { get; set; } = 3;

    // v2 value; 1.0 disables headshots.
    public double HeadshotMultiplier { get; set; } = 1.5;

    // §6.9: the only commands members can run during HUB/IN_PROGRESS.
    public string AllowedCommands { get; set; } = "/siege,/msg,/r,/staffchat,/menu";

    // §6.6: delay before the spawn picker opens after respawn.
    public int SpawnPickerDelayTicks { get; set; } = 20;

    // ---- Enchant-book drops (§9.4, D7) ----
    // Chance per second, in per-mille (v2: 30‰).
    public int EnchantDropChancePerMille { get; set; } = 30;

    // v2 used every WEAPON/WEARABLE/BOW/BREAKABLE enchantment of the running server; this default is
    // that set's combat subset, minus curses and mending.
    public string AllowedEnchantmentKeys { get; set; } = DefaultAllowedEnchantmentKeys;
    public int EnchantLevelMin { get; set; } = 1;
    public int EnchantLevelMax { get; set; } = 2;

    // New (v2 had no cap): books on the ground per match at most.
    public int MaxBooksAlive { get; set; } = 10;

    // ---- Non-member gate view (§8.5, D6) ----
    public SiegeNonMemberGateView NonMemberGateView { get; set; } = SiegeNonMemberGateView.PreLockdownView;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public const string DefaultAllowedEnchantmentKeys =
        "minecraft:sharpness,minecraft:smite,minecraft:bane_of_arthropods,minecraft:knockback," +
        "minecraft:fire_aspect,minecraft:sweeping_edge,minecraft:protection,minecraft:fire_protection," +
        "minecraft:blast_protection,minecraft:projectile_protection,minecraft:thorns," +
        "minecraft:feather_falling,minecraft:power,minecraft:punch,minecraft:flame,minecraft:infinity," +
        "minecraft:unbreaking";
}
