using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Statistics;

namespace knkwebapi_v2.Services.Leaderboards
{
    /// <summary>
    /// One leaderboard (DESIGN.md §F.11): a catalogue metric, optionally one context
    /// (<c>&lt;metric&gt;@&lt;context&gt;</c>); null context = the total across contexts.
    /// </summary>
    public sealed record LeaderboardBoardDefinition(string BoardKey, StatisticMetricDefinition Metric, string? Context, string Label)
    {
        /// <summary>Always-public metrics rank everyone; others need the player's Everyone setting.</summary>
        public bool AlwaysPublic => Metric.Visibility == StatisticVisibilityKind.AlwaysPublic;

        public string? SettingKey => Metric.SettingKey;

        public StatisticAggregation Aggregation => Metric.Aggregation;

        public StatisticUnit Unit => Metric.Unit;

        /// <summary>The discoveries board counts user_domain_discoveries instead of stored rows.</summary>
        public bool IsDiscoveries => Metric.Key == StatisticsCatalog.Discoveries;
    }

    /// <summary>
    /// The leaderboards of DESIGN.md §F.11 / "Leaderboards (recommendation adopted)": active
    /// playtime, XP gained, PvP and PvE kills (total + per context), wins and objectives per
    /// minigame, gate-door damage, distance per mode, discoveries and highest killstreak (overall +
    /// per context), each weekly, monthly and lifetime. Not ranked: deaths, damage received,
    /// logins, AFK time, balances. Pure and immutable.
    /// </summary>
    public static class LeaderboardCatalog
    {
        /// <summary>Minigame contexts (wins/objectives boards). Siege is the only minigame today.</summary>
        public static IReadOnlyList<string> MinigameContexts { get; } = new[] { StatisticsCatalog.Siege };

        public static IReadOnlyList<LeaderboardPeriod> Periods { get; } =
            new[] { LeaderboardPeriod.Weekly, LeaderboardPeriod.Monthly, LeaderboardPeriod.Lifetime };

        public static IReadOnlyList<LeaderboardBoardDefinition> Boards { get; } = BuildBoards();

        private static readonly Dictionary<string, LeaderboardBoardDefinition> ByKey =
            Boards.ToDictionary(b => b.BoardKey, StringComparer.Ordinal);

        public static LeaderboardBoardDefinition? Find(string? boardKey) =>
            boardKey != null && ByKey.TryGetValue(boardKey, out var board) ? board : null;

        /// <summary>"weekly" / "monthly" / "lifetime" → period; anything else → null.</summary>
        public static LeaderboardPeriod? ParsePeriod(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
        {
            "weekly" => LeaderboardPeriod.Weekly,
            "monthly" => LeaderboardPeriod.Monthly,
            "lifetime" => LeaderboardPeriod.Lifetime,
            _ => null
        };

        public static string PeriodName(LeaderboardPeriod period) => period.ToString().ToLowerInvariant();

        public static string ContextLabel(string context) => context switch
        {
            StatisticsCatalog.OpenWorld => "Open world",
            StatisticsCatalog.Siege => "Siege",
            _ => string.Join(' ', context.Split('_', StringSplitOptions.RemoveEmptyEntries))
        };

        /// <summary>
        /// The stored metric key that feeds a board in <paramref name="context"/>: PvP kills rank the
        /// capped <c>pvp_kills.ranked</c> except in projection-owned contexts (Siege: the match tables
        /// know no victims, so no cap applies — L5-3).
        /// </summary>
        public static string SourceMetricKey(StatisticMetricDefinition metric, string context) =>
            metric.Key == StatisticsCatalog.PvpKills && !metric.ProjectionOwnedContexts.Contains(context)
                ? StatisticsCatalog.PvpKillsRanked
                : metric.Key;

        /// <summary>Every stored metric key any board reads.</summary>
        public static IReadOnlyList<string> SourceMetricKeys { get; } = Boards
            .Where(b => !b.IsDiscoveries)
            .SelectMany(b => b.Metric.Key == StatisticsCatalog.PvpKills
                ? new[] { StatisticsCatalog.PvpKills, StatisticsCatalog.PvpKillsRanked }
                : new[] { b.Metric.Key })
            .Distinct()
            .ToList();

        private static List<LeaderboardBoardDefinition> BuildBoards()
        {
            var boards = new List<LeaderboardBoardDefinition>();

            void Total(string metricKey, string? label = null)
            {
                var metric = StatisticsCatalog.FindMetric(metricKey)!;
                boards.Add(new LeaderboardBoardDefinition(metric.Key, metric, null, label ?? metric.Label));
            }

            void PerContext(string metricKey, IEnumerable<string> contexts, string? label = null)
            {
                var metric = StatisticsCatalog.FindMetric(metricKey)!;
                foreach (var context in contexts)
                {
                    boards.Add(new LeaderboardBoardDefinition($"{metric.Key}@{context}", metric, context,
                        $"{label ?? metric.Label} — {ContextLabel(context)}"));
                }
            }

            Total(StatisticsCatalog.ActivePlaytime);
            Total(StatisticsCatalog.XpGained);
            Total(StatisticsCatalog.PvpKills);
            PerContext(StatisticsCatalog.PvpKills, StatisticsCatalog.KnownContexts);
            Total(StatisticsCatalog.PveKills);
            PerContext(StatisticsCatalog.PveKills, StatisticsCatalog.KnownContexts);
            PerContext(StatisticsCatalog.Wins, MinigameContexts);
            PerContext(StatisticsCatalog.ObjectivesCaptured, MinigameContexts);
            Total("gate_damage");
            Total("distance.foot");
            Total("distance.flying");
            Total("distance.vehicle");
            Total(StatisticsCatalog.Discoveries);
            Total(StatisticsCatalog.HighestKillstreak);
            PerContext(StatisticsCatalog.HighestKillstreak, StatisticsCatalog.KnownContexts);
            return boards;
        }
    }
}
