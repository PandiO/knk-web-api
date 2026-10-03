using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>
    /// Statistics reads (KNG-34, DESIGN.md §F.3-§F.4, IMPLEMENTATION_PLAN.md §3.1, §4). Lifetime
    /// reads use player_stat_totals, day/week/month the daily rows. The player's merged identities
    /// (MERGE_FORFEIT ledger rows) are aggregated in (L1-16); their own visibility settings are
    /// not — the surviving account's settings decide. Visibility per viewer:
    /// <list type="bullet">
    /// <item>anonymous — the always-public profile and always-public metrics only (L1-3);</item>
    /// <item>signed-in — plus every setting whose effective value is Everyone (context rule, total
    /// rule: StatisticsVisibilityRules);</item>
    /// <item>self / staff (knk.admin.statistics.view) — everything except internal metrics;
    /// deaths per context only for staff.</item>
    /// </list>
    /// </summary>
    public class StatisticsQueryService : IStatisticsQueryService
    {
        public const int MaxSeriesPoints = 366;

        private readonly IStatisticsRepository _repo;
        private readonly ICurrencyService _currency;
        private readonly ITitleService _titles;
        private readonly IDiscoveryRepository _discoveries;
        private readonly StatisticsOptions _options;
        private readonly TimeProvider _time;

        public StatisticsQueryService(IStatisticsRepository repo, ICurrencyService currency, ITitleService titles,
            IDiscoveryRepository discoveries, IOptions<StatisticsOptions>? options = null, TimeProvider? time = null)
        {
            _repo = repo;
            _currency = currency;
            _titles = titles;
            _discoveries = discoveries;
            _options = options?.Value ?? new StatisticsOptions();
            _time = time ?? TimeProvider.System;
        }

        private TimeZoneInfo Zone => StatisticsPeriods.FindZone(_options.TimeZone);

        private string ZoneId => Zone == TimeZoneInfo.Utc ? "UTC" : _options.TimeZone;

        // ------------------------------------------------------------------ catalogue

        public StatisticsCatalogDto GetCatalog() => new()
        {
            TimeZone = ZoneId,
            Contexts = StatisticsCatalog.KnownContexts.ToList(),
            Metrics = StatisticsCatalog.Metrics
                .Where(m => m.Visibility != StatisticVisibilityKind.Internal)
                .Select(m => new StatisticsCatalogMetricDto
                {
                    Key = m.Key,
                    SettingKey = m.SettingKey,
                    Aggregation = m.Aggregation,
                    Unit = m.Unit,
                    Contextual = m.Contextual && m.Key != StatisticsCatalog.Deaths,
                    Visibility = m.Visibility.ToString(),
                    Group = m.Group,
                    Label = m.Label
                }).ToList(),
            Settings = StatisticsCatalog.Settings.Select(s => new StatisticsCatalogSettingDto
            {
                SettingKey = s.SettingKey, Group = s.Group, Label = s.Label, Contextual = s.Contextual
            }).ToList(),
            Groups = StatisticsCatalog.Groups.Select(g => new StatisticsCatalogGroupDto
            {
                Key = g.Key, Label = g.Label, SettingKeys = g.SettingKeys.ToList()
            }).ToList()
        };

        // ------------------------------------------------------------------ player statistics

        public async Task<PlayerStatisticsDto?> GetAsync(int userId, StatisticsViewer viewer, string? period, DateOnly? date,
            CancellationToken ct = default)
        {
            var periodName = (period ?? "lifetime").Trim().ToLowerInvariant();
            StatisticsPeriodKind? kind = periodName switch
            {
                "lifetime" => null,
                "day" => StatisticsPeriodKind.Day,
                "week" => StatisticsPeriodKind.Week,
                "month" => StatisticsPeriodKind.Month,
                _ => throw new StatisticsValidationException("InvalidPeriod", "period must be lifetime, day, week or month.")
            };

            var user = await _repo.GetUserAsync(userId, ct);
            if (user == null || !user.IsActive) return null;
            var ids = await IdentityIdsAsync(userId, ct);
            var visibility = await _repo.GetVisibilityAsync(userId, ct: ct);
            var totals = await _repo.GetTotalsAsync(ids, ct);

            var dto = new PlayerStatisticsDto
            {
                UserId = user.Id,
                Username = user.Username,
                Period = periodName,
                TimeZone = ZoneId,
                Viewer = viewer.Name,
                Profile = await BuildProfileAsync(user, ids, totals, ct)
            };

            List<StatisticValueRow> rows;
            (DateOnly Start, DateOnly End)? range = null;
            if (kind == null)
            {
                rows = totals;
            }
            else
            {
                var anchor = date ?? StatisticsPeriods.LocalDay(_time.GetUtcNow().UtcDateTime, Zone);
                range = StatisticsPeriods.Resolve(anchor, kind.Value);
                dto.PeriodStart = range.Value.Start;
                dto.PeriodEndExclusive = range.Value.End;
                rows = await _repo.GetDailyAsync(ids, range.Value.Start, range.Value.End, ct: ct);
            }
            var values = Aggregate(rows);

            foreach (var metric in StatisticsCatalog.Metrics)
            {
                if (!metric.IsStored || metric.Visibility == StatisticVisibilityKind.Internal) continue;
                if (metric.SettingKey == StatisticsCatalog.SettingEconomy) continue; // the economy section
                var item = BuildMetric(metric, values, viewer, visibility);
                if (item != null) dto.Metrics.Add(item);
            }

            if (CanSee(viewer, visibility, StatisticsCatalog.SettingEconomy))
            {
                dto.Economy = new PlayerStatisticsEconomyDto
                {
                    CoinsEarned = (long)Value(values, StatisticsCatalog.CoinsEarned),
                    CoinsSpent = (long)Value(values, StatisticsCatalog.CoinsSpent),
                    GemsEarned = (long)Value(values, StatisticsCatalog.GemsEarned),
                    GemsSpent = (long)Value(values, StatisticsCatalog.GemsSpent)
                };
            }

            if (CanSee(viewer, visibility, StatisticsCatalog.SettingDiscoveryCounts))
            {
                dto.Discoveries = await BuildDiscoveryCountsAsync(ids, range, ct);
            }
            return dto;
        }

        private async Task<PlayerStatisticsProfileDto> BuildProfileAsync(User user, List<int> ids, List<StatisticValueRow> totals,
            CancellationToken ct)
        {
            var title = await _titles.ResolveAsync(user.ExperiencePoints, user.Gender);
            return new PlayerStatisticsProfileDto
            {
                TitleName = title.TitleName,
                TitleBracketId = title.TitleBracketId,
                Experience = user.ExperiencePoints,
                Coins = user.Coins,
                Gems = user.Gems,
                FirstJoinedAt = await FirstJoinedAsync(ids, ct),
                ActivePlaytimeSeconds = (long)Math.Floor(totals.Where(t => t.MetricKey == StatisticsCatalog.ActivePlaytime).Sum(t => t.Value)),
                AfkSeconds = (long)Math.Floor(totals.Where(t => t.MetricKey == StatisticsCatalog.AfkTime).Sum(t => t.Value))
            };
        }

        /// <summary>
        /// First joined (DESIGN.md §F.3, L1-15): the earliest of the Minecraft-created accounts'
        /// CreatedAt (the account is created on the first join) and the first recorded session,
        /// across the merged identities. Web-first accounts get a value from their first session;
        /// never joined → null.
        /// </summary>
        private async Task<DateTime?> FirstJoinedAsync(List<int> ids, CancellationToken ct)
        {
            var candidates = new List<DateTime>();
            var users = await _repo.GetUsersAsync(ids, ct);
            candidates.AddRange(users.Where(u => u.AccountCreatedVia == AccountCreationMethod.MinecraftServer).Select(u => u.CreatedAt));
            var profiles = await _repo.GetProfilesAsync(ids, ct);
            candidates.AddRange(profiles.Where(p => p.FirstSessionAt != null).Select(p => p.FirstSessionAt!.Value));
            return candidates.Count == 0 ? null : DateTime.SpecifyKind(candidates.Min(), DateTimeKind.Utc);
        }

        private static PlayerStatisticMetricDto? BuildMetric(StatisticMetricDefinition metric,
            Dictionary<(string Metric, string Context), decimal> values, StatisticsViewer viewer,
            IReadOnlyCollection<PlayerStatVisibility> visibility)
        {
            var contexts = values.Keys.Where(k => k.Metric == metric.Key).Select(k => k.Context)
                .OrderBy(ContextOrder).ThenBy(c => c, StringComparer.Ordinal).ToList();

            bool totalVisible;
            Func<string, bool> contextVisible;
            if (metric.Visibility == StatisticVisibilityKind.AlwaysPublic || viewer.SeesEverything)
            {
                totalVisible = true;
                contextVisible = _ => true;
            }
            else if (viewer.Kind == StatisticsViewerKind.Anonymous || metric.SettingKey == null)
            {
                return null;
            }
            else
            {
                totalVisible = StatisticsVisibilityRules.IsPublic(visibility, metric.SettingKey);
                contextVisible = c => StatisticsVisibilityRules.IsPublic(visibility, metric.SettingKey, c);
            }

            // Deaths are stored per context but shown as a total only, except to staff (DESIGN.md §F.1).
            var showContexts = metric.Contextual && (metric.Key != StatisticsCatalog.Deaths || viewer.Kind == StatisticsViewerKind.Staff);
            var visibleContexts = showContexts ? contexts.Where(c => c != "" && contextVisible(c)).ToList() : new List<string>();
            if (!totalVisible && visibleContexts.Count == 0) return null;

            var total = Combine(metric, contexts.Select(c => values[(metric.Key, c)]));
            return new PlayerStatisticMetricDto
            {
                Key = metric.Key,
                SettingKey = metric.SettingKey,
                Value = totalVisible ? StatisticsFormatting.Display(metric, total) : null,
                RawValue = totalVisible ? total : null,
                Unit = metric.Unit,
                Aggregation = metric.Aggregation,
                Contexts = showContexts
                    ? visibleContexts.Select(c => new PlayerStatisticContextValueDto
                    {
                        Context = c,
                        Value = StatisticsFormatting.Display(metric, values[(metric.Key, c)]),
                        RawValue = values[(metric.Key, c)]
                    }).ToList()
                    : null
            };
        }

        private async Task<PlayerStatisticsDiscoveriesDto> BuildDiscoveryCountsAsync(List<int> ids, (DateOnly Start, DateOnly End)? range,
            CancellationToken ct)
        {
            var discovered = FirstPerDomain(await _repo.GetDiscoveriesAsync(ids, ct));
            if (range != null)
            {
                var from = StatisticsPeriods.StartOfDayUtc(range.Value.Start, Zone);
                var to = StatisticsPeriods.StartOfDayUtc(range.Value.End, Zone);
                discovered = discovered.Where(d => d.DiscoveredAt >= from && d.DiscoveredAt < to).ToList();
            }
            var types = (await _discoveries.GetDomainNodesAsync(discovered.Select(d => d.DomainId).ToList()))
                .ToDictionary(n => n.Id, n => n.DomainType);
            int Count(params string[] domainTypes) =>
                discovered.Count(d => types.TryGetValue(d.DomainId, out var type) && domainTypes.Contains(type));
            return new PlayerStatisticsDiscoveriesDto
            {
                Total = discovered.Count,
                Towns = Count(DiscoveryRewardRule.Town),
                Districts = Count(DiscoveryRewardRule.District),
                Structures = Count(DiscoveryRewardRule.Structure, DiscoveryRewardRule.GateStructure)
            };
        }

        // ------------------------------------------------------------------ series

        public async Task<StatisticSeriesDto?> GetSeriesAsync(int userId, StatisticsViewer viewer, string? metricKey, string? context,
            string? granularity, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            var metric = StatisticsCatalog.FindMetric(metricKey);
            if (metric == null || !metric.IsStored || metric.Visibility == StatisticVisibilityKind.Internal)
            {
                throw new StatisticsValidationException("UnknownMetric", $"Unknown statistics metric '{metricKey}'.");
            }
            var contextKey = context ?? "";
            if (contextKey.Length > 0 && (!metric.Contextual || !StatisticsCatalog.IsValidContext(contextKey)))
            {
                throw new StatisticsValidationException("InvalidContext", $"'{contextKey}' is not a context of '{metric.Key}'.");
            }
            var name = (granularity ?? "day").Trim().ToLowerInvariant();
            var kind = name switch
            {
                "day" => StatisticsPeriodKind.Day,
                "week" => StatisticsPeriodKind.Week,
                "month" => StatisticsPeriodKind.Month,
                _ => throw new StatisticsValidationException("InvalidGranularity", "granularity must be day, week or month.")
            };

            var user = await _repo.GetUserAsync(userId, ct);
            if (user == null || !user.IsActive) return null;
            var visibility = await _repo.GetVisibilityAsync(userId, ct: ct);
            if (!CanSeeMetric(metric, viewer, visibility, contextKey.Length == 0 ? null : contextKey))
            {
                throw new StatisticsHiddenException($"{user.Username}'s {metric.Label.ToLowerInvariant()} is not visible to you.");
            }

            var last = StatisticsPeriods.Resolve(to ?? StatisticsPeriods.LocalDay(_time.GetUtcNow().UtcDateTime, Zone), kind);
            var firstAnchor = from ?? kind switch
            {
                StatisticsPeriodKind.Day => last.Start.AddDays(-29),
                StatisticsPeriodKind.Week => last.Start.AddDays(-7 * 11),
                _ => last.Start.AddMonths(-11)
            };
            var first = StatisticsPeriods.Resolve(firstAnchor, kind);
            if (first.Start > last.Start)
            {
                throw new StatisticsValidationException("InvalidRange", "from must not be after to.");
            }

            var starts = new List<DateOnly>();
            for (var start = first.Start; start <= last.Start; start = StatisticsPeriods.Resolve(start, kind).EndExclusive)
            {
                starts.Add(start);
                if (starts.Count > MaxSeriesPoints)
                {
                    throw new StatisticsValidationException("RangeTooLarge", $"A series has at most {MaxSeriesPoints} points.");
                }
            }

            var ids = await IdentityIdsAsync(userId, ct);
            var rows = (await _repo.GetDailyAsync(ids, first.Start, last.EndExclusive, metric.Key, ct))
                .Where(r => contextKey.Length == 0 || r.ContextKey == contextKey)
                .ToList();
            var byPeriod = rows.GroupBy(r => StatisticsPeriods.Resolve(r.Day!.Value, kind).Start)
                .ToDictionary(g => g.Key, g => CombineDays(metric, g));

            return new StatisticSeriesDto
            {
                Metric = metric.Key,
                Context = contextKey,
                Granularity = name,
                Points = starts.Select(s => new StatisticSeriesPointDto
                {
                    PeriodStart = s,
                    Value = StatisticsFormatting.Display(metric, byPeriod.TryGetValue(s, out var v) ? v : 0m)
                }).ToList()
            };
        }

        // ------------------------------------------------------------------ title history, discoveries

        public async Task<PagedResultDto<TitleChangeDto>?> GetTitleHistoryAsync(int userId, StatisticsViewer viewer, int page, int pageSize,
            CancellationToken ct = default)
        {
            var user = await RequireVisibleSettingAsync(userId, viewer, StatisticsCatalog.SettingTitleHistory, "title history", ct);
            if (user == null) return null;
            var (pageNumber, size) = Paging(page, pageSize);
            var ids = await IdentityIdsAsync(userId, ct);
            var (items, total) = await _repo.GetTitleChangesAsync(ids, (pageNumber - 1) * size, size, ct);
            return new PagedResultDto<TitleChangeDto>
            {
                Items = items.Select(c => new TitleChangeDto
                {
                    ChangedAt = DateTime.SpecifyKind(c.ChangedAt, DateTimeKind.Utc),
                    FromTitleName = c.FromTitleName,
                    ToTitleName = c.ToTitleName,
                    Direction = c.Direction
                }).ToList(),
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = size
            };
        }

        public async Task<PagedResultDto<DiscoveryListItemDto>?> GetDiscoveriesAsync(int userId, StatisticsViewer viewer, int page, int pageSize,
            CancellationToken ct = default)
        {
            var user = await RequireVisibleSettingAsync(userId, viewer, StatisticsCatalog.SettingDiscoveryList, "discovered places", ct);
            if (user == null) return null;
            var (pageNumber, size) = Paging(page, pageSize);
            var ids = await IdentityIdsAsync(userId, ct);
            var discovered = FirstPerDomain(await _repo.GetDiscoveriesAsync(ids, ct));
            var nodes = (await _discoveries.GetDomainNodesAsync(discovered.Select(d => d.DomainId).ToList())).ToDictionary(n => n.Id);
            var listed = discovered.Where(d => nodes.ContainsKey(d.DomainId))
                .OrderByDescending(d => d.DiscoveredAt).ThenByDescending(d => d.DomainId)
                .ToList();
            return new PagedResultDto<DiscoveryListItemDto>
            {
                Items = listed.Skip((pageNumber - 1) * size).Take(size).Select(d => new DiscoveryListItemDto
                {
                    DomainId = d.DomainId,
                    Name = nodes[d.DomainId].Name,
                    DomainType = nodes[d.DomainId].DomainType,
                    DiscoveredAt = DateTime.SpecifyKind(d.DiscoveredAt, DateTimeKind.Utc)
                }).ToList(),
                TotalCount = listed.Count,
                PageNumber = pageNumber,
                PageSize = size
            };
        }

        private async Task<User?> RequireVisibleSettingAsync(int userId, StatisticsViewer viewer, string settingKey, string what,
            CancellationToken ct)
        {
            var user = await _repo.GetUserAsync(userId, ct);
            if (user == null || !user.IsActive) return null;
            var visibility = await _repo.GetVisibilityAsync(userId, ct: ct);
            if (!CanSee(viewer, visibility, settingKey))
            {
                throw new StatisticsHiddenException($"{user.Username}'s {what} is not visible to you.");
            }
            return user;
        }

        // ------------------------------------------------------------------ helpers

        private async Task<List<int>> IdentityIdsAsync(int userId, CancellationToken ct)
        {
            var ids = new List<int> { userId };
            ids.AddRange(await _currency.GetMergedAccountIdsAsync(userId, ct));
            return ids.Distinct().ToList();
        }

        private static bool CanSee(StatisticsViewer viewer, IReadOnlyCollection<PlayerStatVisibility> visibility, string settingKey) =>
            viewer.SeesEverything
            || (viewer.Kind == StatisticsViewerKind.SignedIn && StatisticsVisibilityRules.IsPublic(visibility, settingKey));

        private static bool CanSeeMetric(StatisticMetricDefinition metric, StatisticsViewer viewer,
            IReadOnlyCollection<PlayerStatVisibility> visibility, string? context)
        {
            if (metric.Key == StatisticsCatalog.Deaths && context != null && viewer.Kind != StatisticsViewerKind.Staff) return false;
            if (metric.Visibility == StatisticVisibilityKind.AlwaysPublic || viewer.SeesEverything) return true;
            return viewer.Kind == StatisticsViewerKind.SignedIn && metric.SettingKey != null
                && StatisticsVisibilityRules.IsPublic(visibility, metric.SettingKey, context);
        }

        /// <summary>Per (metric, context): rows of all identities (and days) combined by the metric's aggregation.</summary>
        private static Dictionary<(string Metric, string Context), decimal> Aggregate(IEnumerable<StatisticValueRow> rows)
        {
            var result = new Dictionary<(string, string), decimal>();
            foreach (var row in rows)
            {
                var metric = StatisticsCatalog.FindMetric(row.MetricKey);
                if (metric == null) continue;
                var key = (row.MetricKey, row.ContextKey);
                result[key] = result.TryGetValue(key, out var current)
                    ? metric.Aggregation == StatisticAggregation.Max ? Math.Max(current, row.Value) : current + row.Value
                    : row.Value;
            }
            return result;
        }

        private static decimal CombineDays(StatisticMetricDefinition metric, IEnumerable<StatisticValueRow> rows) =>
            Combine(metric, rows.Select(r => r.Value));

        private static decimal Combine(StatisticMetricDefinition metric, IEnumerable<decimal> values)
        {
            var list = values.ToList();
            if (list.Count == 0) return 0m;
            return metric.Aggregation == StatisticAggregation.Max ? list.Max() : list.Sum();
        }

        private static decimal Value(Dictionary<(string Metric, string Context), decimal> values, string metric) =>
            values.TryGetValue((metric, ""), out var value) ? value : 0m;

        /// <summary>One discovery per domain across merged identities: the earliest.</summary>
        private static List<UserDomainDiscovery> FirstPerDomain(IEnumerable<UserDomainDiscovery> discoveries) =>
            discoveries.GroupBy(d => d.DomainId).Select(g => g.OrderBy(d => d.DiscoveredAt).First()).ToList();

        private static (int Page, int Size) Paging(int page, int pageSize) => (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

        private static int ContextOrder(string context)
        {
            if (context == "") return -1;
            var index = StatisticsCatalog.KnownContexts.ToList().IndexOf(context);
            return index < 0 ? int.MaxValue : index;
        }
    }
}
