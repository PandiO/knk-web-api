using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>
    /// Effective visibility of a player's statistics settings (KNG-34, DESIGN.md §F.4, D8, L1-3/L1-4).
    /// Pure.
    /// <list type="bullet">
    /// <item>No row = Nobody. Friends fails closed (= Nobody) until KNG-35.</item>
    /// <item>A context: its override row if present, else the metric-level row.</item>
    /// <item>A total across contexts: public only when the metric-level value and every context
    /// override are Everyone — a hidden context must not leak through the total.</item>
    /// </list>
    /// </summary>
    public static class StatisticsVisibilityRules
    {
        /// <summary>The stored metric-level value (Nobody when never set).</summary>
        public static StatisticVisibility MetricLevel(IEnumerable<PlayerStatVisibility> rows, string settingKey) =>
            rows.FirstOrDefault(r => r.SettingKey == settingKey && r.ContextKey == "")?.Visibility ?? StatisticVisibility.Nobody;

        /// <summary>The stored value that applies to a context: the override, else the inherited metric level.</summary>
        public static StatisticVisibility ForContext(IEnumerable<PlayerStatVisibility> rows, string settingKey, string context)
        {
            var list = rows as IReadOnlyCollection<PlayerStatVisibility> ?? rows.ToList();
            var overrideRow = list.FirstOrDefault(r => r.SettingKey == settingKey && r.ContextKey == context);
            return overrideRow?.Visibility ?? MetricLevel(list, settingKey);
        }

        /// <summary>What a stored value means for other players today: Friends shows nothing (KNG-35 pending).</summary>
        public static StatisticVisibility Effective(StatisticVisibility stored) =>
            stored == StatisticVisibility.Friends ? StatisticVisibility.Nobody : stored;

        /// <summary>
        /// Whether a signed-in other player may see the setting's value — in <paramref name="context"/>,
        /// or (null) the total across contexts. Non-contextual settings ignore the context.
        /// </summary>
        public static bool IsPublic(IEnumerable<PlayerStatVisibility> rows, string settingKey, string? context = null)
        {
            var list = rows as IReadOnlyCollection<PlayerStatVisibility> ?? rows.ToList();
            var setting = StatisticsCatalog.FindSetting(settingKey);
            if (setting == null) return false;
            if (!setting.Contextual)
            {
                return Effective(MetricLevel(list, settingKey)) == StatisticVisibility.Everyone;
            }
            if (!string.IsNullOrEmpty(context))
            {
                return Effective(ForContext(list, settingKey, context)) == StatisticVisibility.Everyone;
            }
            return Effective(MetricLevel(list, settingKey)) == StatisticVisibility.Everyone
                && list.Where(r => r.SettingKey == settingKey && r.ContextKey != "")
                    .All(r => Effective(r.Visibility) == StatisticVisibility.Everyone);
        }
    }
}
