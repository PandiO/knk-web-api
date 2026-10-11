using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>Who may see a metric (DESIGN.md §F.1): always public, per the player's setting, or
    /// never returned to players.</summary>
    public enum StatisticVisibilityKind
    {
        AlwaysPublic,
        Configurable,
        Internal
    }

    /// <summary>Where a metric's value comes from (DESIGN.md §F.0): plugin facts, the plugin's
    /// session entries, the currency ledger projection, the Siege match projection, or derived at
    /// read time from another table.</summary>
    public enum StatisticSource
    {
        Plugin,
        Session,
        Ledger,
        SiegeProjection,
        Derived
    }

    /// <summary>Which list of a plugin batch may carry a metric (IMPLEMENTATION_PLAN.md §3.1).
    /// <see cref="None"/>: the plugin can't write it at all.</summary>
    public enum StatisticPluginInput
    {
        None,
        Counter,
        Record,
        Duration,
        PvpKill
    }

    /// <summary>One metric of the catalogue (IMPLEMENTATION_PLAN.md §2).</summary>
    public sealed record StatisticMetricDefinition(
        string Key,
        string? SettingKey,
        StatisticAggregation Aggregation,
        StatisticUnit Unit,
        bool Contextual,
        StatisticVisibilityKind Visibility,
        StatisticSource Source,
        StatisticPluginInput PluginInput,
        IReadOnlySet<string> ProjectionOwnedContexts,
        decimal MaxPerEntry,
        string Group,
        string Label)
    {
        /// <summary>The value lives in player_stat_daily/totals (not derived at read time).</summary>
        public bool IsStored => Source != StatisticSource.Derived;
    }

    /// <summary>A player-chosen visibility setting (DESIGN.md §F.1 "Visibility setting keys").</summary>
    public sealed record StatisticSettingDefinition(string SettingKey, string Group, string Label, bool Contextual);

    /// <summary>A menu group of settings (DESIGN.md §F.1 "Menu groups").</summary>
    public sealed record StatisticGroupDefinition(string Key, string Label, IReadOnlyList<string> SettingKeys);

    /// <summary>
    /// The statistics catalogue (KNG-34, knk-workspace docs/specs/player-statistics/DESIGN.md §F.1,
    /// IMPLEMENTATION_PLAN.md §2): every metric key, its aggregation, unit, context breakdown,
    /// visibility, source and limits, plus the visibility settings and their menu groups. Keys are
    /// stable identifiers shared with the plugin's StatisticsMetric enum. Pure and immutable.
    /// <para>
    /// Not metrics here (they have no stored value): "first joined" (a profile field, §F.3), the
    /// discovered-places list (setting <c>discoveries.list</c>) and the title history (setting
    /// <c>title_history</c>).
    /// </para>
    /// </summary>
    public static class StatisticsCatalog
    {
        public const string OpenWorld = "open_world";
        public const string Siege = "siege";

        public const string GroupActivity = "activity";
        public const string GroupCombat = "combat";
        public const string GroupMinigames = "minigames";
        public const string GroupExploration = "exploration";
        public const string GroupProgression = "progression";

        // Metric keys used by name in the services.
        public const string ActivePlaytime = "active_playtime";
        public const string AfkTime = "afk_time";
        public const string Logins = "logins";
        public const string PvpKills = "pvp_kills";

        /// <summary>Internal: PvP kills that count toward pvp_kills leaderboards — at most
        /// Leaderboards:RepeatVictimDailyCap per victim per killer per local day (§F.11), maintained at
        /// ingestion so every period (incl. lifetime, after kill pairs expire) reads it from daily/totals.</summary>
        public const string PvpKillsRanked = "pvp_kills.ranked";
        public const string PveKills = "pve_kills";
        public const string Deaths = "deaths";
        public const string HighestKillstreak = "highest_killstreak";
        public const string Wins = "wins";
        public const string Losses = "losses";
        public const string Draws = "draws";
        public const string ObjectivesCaptured = "objectives_captured";
        public const string HighestFall = "highest_fall";
        public const string XpGained = "xp_gained";
        public const string CoinsEarned = "coins_earned";
        public const string CoinsSpent = "coins_spent";
        public const string GemsEarned = "gems_earned";
        public const string GemsSpent = "gems_spent";
        public const string Discoveries = "discoveries";

        // Setting keys used by name in the services.
        public const string SettingEconomy = "economy";
        public const string SettingDiscoveryCounts = "discoveries.counts";
        public const string SettingDiscoveryList = "discoveries.list";
        public const string SettingTitleHistory = "title_history";

        public const decimal MaxDurationSeconds = 86_400m;
        private const decimal MaxCount = 10_000m;
        private const decimal MaxDistance = 100_000m;
        private const decimal MaxDamage = 100_000m;
        private const decimal MaxFall = 10_000m;

        private static readonly Regex ContextPattern = new("^[a-z][a-z0-9_]{0,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly IReadOnlySet<string> NoContexts = new HashSet<string>();
        private static readonly IReadOnlySet<string> SiegeOwned = new HashSet<string> { Siege };

        /// <summary>Contexts that exist today; others matching the pattern are accepted too (§F.1).</summary>
        public static IReadOnlyList<string> KnownContexts { get; } = new[] { OpenWorld, Siege };

        public static IReadOnlyList<StatisticMetricDefinition> Metrics { get; } = BuildMetrics();

        public static IReadOnlyList<StatisticSettingDefinition> Settings { get; } = BuildSettings();

        public static IReadOnlyList<StatisticGroupDefinition> Groups { get; } = new[]
        {
            Group(GroupActivity, "Activity"),
            Group(GroupCombat, "Combat"),
            Group(GroupMinigames, "Minigames"),
            Group(GroupExploration, "Exploration"),
            Group(GroupProgression, "Progression"),
        };

        private static readonly Dictionary<string, StatisticMetricDefinition> MetricsByKey =
            Metrics.ToDictionary(m => m.Key, StringComparer.Ordinal);

        private static readonly Dictionary<string, StatisticSettingDefinition> SettingsByKey =
            Settings.ToDictionary(s => s.SettingKey, StringComparer.Ordinal);

        public static IReadOnlyList<string> SettingKeys { get; } = Settings.Select(s => s.SettingKey).ToList();

        public static IReadOnlyList<string> ContextualSettingKeys { get; } =
            Settings.Where(s => s.Contextual).Select(s => s.SettingKey).ToList();

        public static StatisticMetricDefinition? FindMetric(string? key) =>
            key != null && MetricsByKey.TryGetValue(key, out var metric) ? metric : null;

        public static StatisticSettingDefinition? FindSetting(string? key) =>
            key != null && SettingsByKey.TryGetValue(key, out var setting) ? setting : null;

        /// <summary>Metrics a setting controls (e.g. "damage_dealt" → damage_dealt.player, .mob).</summary>
        public static IReadOnlyList<StatisticMetricDefinition> MetricsForSetting(string settingKey) =>
            Metrics.Where(m => m.SettingKey == settingKey).ToList();

        /// <summary>A syntactically valid context key (<c>^[a-z][a-z0-9_]{0,31}$</c>).</summary>
        public static bool IsValidContext(string? context) => context != null && ContextPattern.IsMatch(context);

        /// <summary>
        /// Whether the plugin may send <paramref name="metric"/> in <paramref name="context"/>:
        /// false for ledger/derived/projection metrics everywhere, and for the Siege-owned pairs
        /// (pvp_kills, deaths, highest_killstreak in "siege" — the match tables count them, §F.6).
        /// Context validity is checked separately.
        /// </summary>
        public static bool IsPluginWritable(string metric, string context)
        {
            var definition = FindMetric(metric);
            return definition != null
                && definition.PluginInput != StatisticPluginInput.None
                && !definition.ProjectionOwnedContexts.Contains(context);
        }

        private static StatisticGroupDefinition Group(string key, string label) =>
            new(key, label, BuildSettings().Where(s => s.Group == key).Select(s => s.SettingKey).ToList());

        private static List<StatisticMetricDefinition> BuildMetrics()
        {
            var list = new List<StatisticMetricDefinition>();

            void Add(string key, string? setting, StatisticAggregation aggregation, StatisticUnit unit, bool contextual,
                StatisticVisibilityKind visibility, StatisticSource source, StatisticPluginInput input, decimal max,
                string group, string label, IReadOnlySet<string>? owned = null) =>
                list.Add(new StatisticMetricDefinition(key, setting, aggregation, unit, contextual, visibility, source, input,
                    owned ?? NoContexts, max, group, label));

            const StatisticAggregation Sum = StatisticAggregation.Sum;
            const StatisticAggregation Max = StatisticAggregation.Max;
            const StatisticVisibilityKind Public = StatisticVisibilityKind.AlwaysPublic;
            const StatisticVisibilityKind Configurable = StatisticVisibilityKind.Configurable;
            const StatisticVisibilityKind Internal = StatisticVisibilityKind.Internal;

            // Activity (sessions, §F.2-§F.3)
            Add(ActivePlaytime, null, Sum, StatisticUnit.Seconds, false, Public, StatisticSource.Session, StatisticPluginInput.Duration, MaxDurationSeconds, GroupActivity, "Active playtime");
            Add(AfkTime, null, Sum, StatisticUnit.Seconds, false, Public, StatisticSource.Session, StatisticPluginInput.Duration, MaxDurationSeconds, GroupActivity, "AFK time");
            Add(Logins, "logins", Sum, StatisticUnit.Count, false, Configurable, StatisticSource.Session, StatisticPluginInput.None, MaxCount, GroupActivity, "Logins");

            // Combat (§F.7); the Siege context of kills/deaths/streak is projected from the match tables.
            Add(PvpKills, "pvp_kills", Sum, StatisticUnit.Count, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.PvpKill, MaxCount, GroupCombat, "Player kills", SiegeOwned);
            Add(PvpKillsRanked, null, Sum, StatisticUnit.Count, true, Internal, StatisticSource.Plugin, StatisticPluginInput.None, MaxCount, GroupCombat, "Player kills (leaderboard-capped)");
            Add(PveKills, "pve_kills", Sum, StatisticUnit.Count, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxCount, GroupCombat, "Creature kills");
            Add(Deaths, "deaths", Sum, StatisticUnit.Count, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxCount, GroupCombat, "Deaths", SiegeOwned);
            Add("deaths_by_cause.player", null, Sum, StatisticUnit.Count, true, Internal, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxCount, GroupCombat, "Deaths caused by players");
            Add("deaths_by_cause.mob", null, Sum, StatisticUnit.Count, true, Internal, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxCount, GroupCombat, "Deaths caused by creatures");
            Add("deaths_by_cause.environment", null, Sum, StatisticUnit.Count, true, Internal, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxCount, GroupCombat, "Deaths caused by the environment");
            Add("damage_dealt.player", "damage_dealt", Sum, StatisticUnit.Points, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxDamage, GroupCombat, "Damage dealt to players");
            Add("damage_dealt.mob", "damage_dealt", Sum, StatisticUnit.Points, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxDamage, GroupCombat, "Damage dealt to creatures");
            Add("damage_received.player", "damage_received", Sum, StatisticUnit.Points, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxDamage, GroupCombat, "Damage received from players");
            Add("damage_received.mob", "damage_received", Sum, StatisticUnit.Points, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxDamage, GroupCombat, "Damage received from creatures");
            Add("arrows_fired", "arrows_fired", Sum, StatisticUnit.Count, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxCount, GroupCombat, "Arrows fired");
            Add("headshots", "headshots", Sum, StatisticUnit.Count, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxCount, GroupCombat, "Headshots");
            Add(HighestKillstreak, "highest_killstreak", Max, StatisticUnit.Count, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.Record, MaxCount, GroupCombat, "Highest killstreak", SiegeOwned);

            // Minigames (§F.6, §F.8)
            Add(Wins, "wins", Sum, StatisticUnit.Count, true, Configurable, StatisticSource.SiegeProjection, StatisticPluginInput.None, MaxCount, GroupMinigames, "Wins");
            Add(Losses, "losses", Sum, StatisticUnit.Count, true, Configurable, StatisticSource.SiegeProjection, StatisticPluginInput.None, MaxCount, GroupMinigames, "Losses");
            Add(Draws, "draws", Sum, StatisticUnit.Count, true, Configurable, StatisticSource.SiegeProjection, StatisticPluginInput.None, MaxCount, GroupMinigames, "Draws");
            Add(ObjectivesCaptured, "objectives_captured", Sum, StatisticUnit.Count, true, Configurable, StatisticSource.SiegeProjection, StatisticPluginInput.None, MaxCount, GroupMinigames, "Objectives captured");
            Add("gate_damage", "gate_damage", Sum, StatisticUnit.Points, true, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxDamage, GroupMinigames, "Gate-door damage");

            // Exploration (§F.9)
            Add("distance.foot", "distance.foot", Sum, StatisticUnit.Blocks, false, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxDistance, GroupExploration, "Distance on foot");
            Add("distance.flying", "distance.flying", Sum, StatisticUnit.Blocks, false, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxDistance, GroupExploration, "Distance flying");
            Add("distance.vehicle", "distance.vehicle", Sum, StatisticUnit.Blocks, false, Configurable, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxDistance, GroupExploration, "Distance in a vehicle");
            Add("distance.swim", null, Sum, StatisticUnit.Blocks, false, Internal, StatisticSource.Plugin, StatisticPluginInput.Counter, MaxDistance, GroupExploration, "Distance swimming");
            Add(HighestFall, "highest_fall", Max, StatisticUnit.Blocks, false, Configurable, StatisticSource.Plugin, StatisticPluginInput.Record, MaxFall, GroupExploration, "Highest survived fall");
            Add(Discoveries, SettingDiscoveryCounts, Sum, StatisticUnit.Count, false, Configurable, StatisticSource.Derived, StatisticPluginInput.None, MaxCount, GroupExploration, "Discoveries");

            // Progression (§F.5): projected from the currency ledger.
            Add(XpGained, null, Sum, StatisticUnit.Count, false, Public, StatisticSource.Ledger, StatisticPluginInput.None, MaxCount, GroupProgression, "XP gained");
            Add(CoinsEarned, SettingEconomy, Sum, StatisticUnit.Count, false, Configurable, StatisticSource.Ledger, StatisticPluginInput.None, MaxCount, GroupProgression, "Coins earned");
            Add(CoinsSpent, SettingEconomy, Sum, StatisticUnit.Count, false, Configurable, StatisticSource.Ledger, StatisticPluginInput.None, MaxCount, GroupProgression, "Coins spent");
            Add(GemsEarned, SettingEconomy, Sum, StatisticUnit.Count, false, Configurable, StatisticSource.Ledger, StatisticPluginInput.None, MaxCount, GroupProgression, "Gems earned");
            Add(GemsSpent, SettingEconomy, Sum, StatisticUnit.Count, false, Configurable, StatisticSource.Ledger, StatisticPluginInput.None, MaxCount, GroupProgression, "Gems spent");

            return list;
        }

        private static List<StatisticSettingDefinition> BuildSettings() => new()
        {
            new("logins", GroupActivity, "Logins", false),
            new("pvp_kills", GroupCombat, "Player kills", true),
            new("pve_kills", GroupCombat, "Creature kills", true),
            new("deaths", GroupCombat, "Deaths", false),
            new("damage_dealt", GroupCombat, "Damage dealt", true),
            new("damage_received", GroupCombat, "Damage received", true),
            new("arrows_fired", GroupCombat, "Arrows fired", true),
            new("headshots", GroupCombat, "Headshots", true),
            new("highest_killstreak", GroupCombat, "Highest killstreak", true),
            new("wins", GroupMinigames, "Wins", true),
            new("losses", GroupMinigames, "Losses", true),
            new("draws", GroupMinigames, "Draws", true),
            new("objectives_captured", GroupMinigames, "Objectives captured", true),
            new("gate_damage", GroupMinigames, "Gate-door damage", true),
            new("distance.foot", GroupExploration, "Distance on foot", false),
            new("distance.flying", GroupExploration, "Distance flying", false),
            new("distance.vehicle", GroupExploration, "Distance in a vehicle", false),
            new("highest_fall", GroupExploration, "Highest survived fall", false),
            new(SettingDiscoveryCounts, GroupExploration, "Discovery counts", false),
            new(SettingDiscoveryList, GroupExploration, "Discovered places", false),
            new(SettingTitleHistory, GroupProgression, "Title history", false),
            new(SettingEconomy, GroupProgression, "Coins and gems earned and spent", false),
        };
    }
}
