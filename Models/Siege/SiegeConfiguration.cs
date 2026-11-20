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
    // Siege Phase 9: A1 and IV A2 carry the developer's playtest tuning (DESIGN §7.2 "Playtest
    // tuning": A1 10, A2 2, IV A2 10, D 6/3/6) instead of the legacy 5/2/5, so a fresh DB starts
    // tuned. An existing configuration row keeps its own values.
    public int CaptureAttackBase { get; set; } = 10;                         // A1 (legacy 5)
    public int CaptureAttackPerExtra { get; set; } = 2;                      // A2
    public int CaptureAttackPerExtraInstantVictory { get; set; } = 10;       // A2 on IV objectives (legacy 5)
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

    // Playtest 2026-10-24 (DESIGN §11.1): seconds of matchmaking left after /siege skip (players with
    // knk.siege.skip) and after /siege admin skip. The plugin never goes below VoteCloseSecondsBeforeStart + 1,
    // so voting keeps its last second (the admin default is the legacy T-31).
    public int PlayerSkipMatchmakingSeconds { get; set; } = 60;
    public int AdminSkipMatchmakingSeconds { get; set; } = 31;

    // Alpha run 1 (PandiO/knk-workspace#198): after a match that ended normally the members wait this long at the
    // scenario hub with the result before they are restored (0 = restored at once, the behaviour before).
    public int PostMatchSeconds { get; set; } = 30;

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

    // ---- Navigation compass (KNG-234) ----
    // Siege players get the navigation compass at the hub unless a scenario or lobby says otherwise.
    // Off by default, so deploying the feature changes nothing until an admin turns it on.
    public bool GiveNavigationItem { get; set; } = false;

    // ---- Siege points (docs/specs/siege-shop/DESIGN.md § 5.2, § 6.1.4; PandiO/knk-workspace#866) ----
    // What PvP Siege pays in siege points, the per-match balance spent in the siege shop. Read by the plugin with
    // the runtime config; it earns only with its siege.points.enabled switch on. Defaults are the design's
    // proposals (about 100 points a minute for a good player), tuned in the alpha balance runs (#651).
    public int PointsPerKill { get; set; } = 100;
    // The n-th kill of a streak pays PointsPerKill × min(1 + step × (n − 1), cap) (P14).
    public double PointsStreakStep { get; set; } = 0.1;
    public double PointsStreakCap { get; set; } = 1.5;
    // An assist: at least AssistMinDamage damage (half hearts) to the victim in the last AssistWindowSeconds (P13).
    public int PointsPerAssist { get; set; } = 40;
    public double AssistMinDamage { get; set; } = 4.0;
    public int AssistWindowSeconds { get; set; } = 10;
    public int PointsPerDeath { get; set; } = 15;
    // One point per this many gate HP removed (0 = gate damage pays nothing).
    public double GateHpPerPoint { get; set; } = 5.0;
    // Per percent an objective moves towards a side, to each of that side's members in the radius (P16).
    public int PointsPerCapturePercent { get; set; } = 3;
    public int PointsPerCapture { get; set; } = 100;
    // Every TrickleIntervalSeconds, to each member who was active in the interval (P17).
    public int TrickleAmount { get; set; } = 10;
    public int TrickleIntervalSeconds { get; set; } = 60;
    // Teammates may send each other siege points (/siege pay, P07): off in PvP Siege.
    public bool PointTransfersEnabled { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public const string DefaultAllowedEnchantmentKeys =
        "minecraft:sharpness,minecraft:smite,minecraft:bane_of_arthropods,minecraft:knockback," +
        "minecraft:fire_aspect,minecraft:sweeping_edge,minecraft:protection,minecraft:fire_protection," +
        "minecraft:blast_protection,minecraft:projectile_protection,minecraft:thorns," +
        "minecraft:feather_falling,minecraft:power,minecraft:punch,minecraft:flame,minecraft:infinity," +
        "minecraft:unbreaking";
}
