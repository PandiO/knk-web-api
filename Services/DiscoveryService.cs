using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Domain discovery grants and reads (docs/specs/domain-discovery/DESIGN.md §3.4/§3.5).
    ///
    /// A grant resolves the plugin's raw WorldGuard region ids to domains on the server, adds
    /// ancestors (IncludeAncestors), drops disabled domains, then - inside a transaction holding a
    /// row lock on the user - skips what the user already discovered, applies the hourly cap,
    /// rolls each reward (DiscoveryRewardCalculator), inserts the unique (UserId, DomainId) rows and
    /// credits the sum through UserService.AdjustBalancesAsync, all on the same scoped DbContext so
    /// rows and balances commit together. Rewards are scaled per currency like KNG-16's title
    /// promotion bonuses (CurrencyMultipliersDto).
    /// </summary>
    public class DiscoveryService : IDiscoveryService
    {
        /// <summary>Most region + domain ids one grant request may carry.</summary>
        public const int MaxIdsPerRequest = 50;

        /// <summary>BalanceAdjusted audit reason of every discovery credit.</summary>
        public const string BalanceReason = "domain-discovery";

        private static readonly DiscoverySource[] ClientSources =
        {
            DiscoverySource.RegionEnter, DiscoverySource.JoinInside, DiscoverySource.Replay
        };

        // No metrics pipeline is wired yet (ObservabilityServiceCollectionExtensions' OpenTelemetry
        // setup is a TODO), so these reach a listener only once one adds the "knk.discovery" meter.
        public const string MeterName = "knk.discovery";
        private static readonly Meter DiscoveryMeter = new(MeterName);
        private static readonly Counter<long> GrantedCounter = DiscoveryMeter.CreateCounter<long>("discoveries_granted");
        private static readonly Counter<long> DuplicateCounter = DiscoveryMeter.CreateCounter<long>("discoveries_duplicate");
        private static readonly Counter<long> RateLimitedCounter = DiscoveryMeter.CreateCounter<long>("discoveries_rate_limited");

        private readonly IDiscoveryRepository _repo;
        private readonly IUserRepository _userRepo;
        private readonly IUserService _userService;
        private readonly ITitleService _titleService;
        private readonly IUserPermissionGroupService _membershipService;
        private readonly IAuditLogService _auditLogService;
        private readonly ILogger<DiscoveryService> _logger;
        private readonly DiscoveryOptions _options;
        private readonly Random _random;

        public DiscoveryService(
            IDiscoveryRepository repo,
            IUserRepository userRepo,
            IUserService userService,
            ITitleService titleService,
            IUserPermissionGroupService membershipService,
            IAuditLogService auditLogService,
            ILogger<DiscoveryService> logger,
            IOptions<DiscoveryOptions>? options = null,
            Random? random = null)
        {
            _repo = repo;
            _userRepo = userRepo;
            _userService = userService;
            _titleService = titleService;
            _membershipService = membershipService;
            _auditLogService = auditLogService;
            _logger = logger;
            _options = options?.Value ?? new DiscoveryOptions();
            _random = random ?? Random.Shared;
        }

        public async Task<DiscoveryGrantResultDto> DiscoverAsync(int userId, DiscoveryGrantRequestDto request)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user id.", nameof(userId));
            if (request == null) throw new ArgumentException("A request body is required.", nameof(request));

            var regionIds = (request.WgRegionIds ?? new List<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var domainIds = (request.DomainIds ?? new List<int>()).Distinct().ToList();
            if (regionIds.Count + domainIds.Count == 0)
            {
                throw new ArgumentException("Send at least one wgRegionId or domainId.", nameof(request));
            }
            if (regionIds.Count + domainIds.Count > MaxIdsPerRequest)
            {
                throw new ArgumentException($"At most {MaxIdsPerRequest} ids per request.", nameof(request));
            }
            var source = ParseSource(request.Source);

            try
            {
                return await _repo.RunLockedForUserAsync(userId, () => GrantAsync(userId, regionIds, domainIds, source));
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                // A concurrent grant inserted one of the same (UserId, DomainId) rows first - the user
                // lock makes this unlikely on MySQL, but the unique index is the real guarantee. The
                // transaction rolled back; run once more so the rows it did commit show up as
                // alreadyDiscovered and anything left is still granted.
                DuplicateCounter.Add(1);
                _repo.ResetTracking();
                return await _repo.RunLockedForUserAsync(userId, () => GrantAsync(userId, regionIds, domainIds, source));
            }
        }

        private async Task<DiscoveryGrantResultDto> GrantAsync(int userId, List<string> regionIds, List<int> domainIds, DiscoverySource source)
        {
            // Read inside the lock, so the balances AdjustBalancesAsync starts from are current.
            var user = await _userRepo.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException($"User with id {userId} not found.");

            var result = new DiscoveryGrantResultDto();

            // 1. Resolve what was sent. Two domains sharing a region id (WgRegionId isn't unique)
            // resolve to the lowest id, deterministically.
            var nodes = new Dictionary<int, DiscoveryDomainNode>();
            var requested = new List<(int DomainId, string Key)>();
            if (regionIds.Count > 0)
            {
                var byRegion = (await _repo.FindByWgRegionIdsAsync(regionIds))
                    .Where(n => n.WgRegionId != null)
                    .GroupBy(n => n.WgRegionId!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Id).First(), StringComparer.OrdinalIgnoreCase);
                foreach (var regionId in regionIds)
                {
                    if (byRegion.TryGetValue(regionId, out var node))
                    {
                        nodes[node.Id] = node;
                        requested.Add((node.Id, regionId));
                    }
                    else
                    {
                        result.Skipped.Add(new DiscoverySkipDto { Key = regionId, Reason = DiscoverySkipDto.NotADomain });
                    }
                }
            }
            if (domainIds.Count > 0)
            {
                foreach (var node in await _repo.GetDomainNodesAsync(domainIds.Where(id => id > 0).ToList()))
                {
                    nodes[node.Id] = node;
                }
                foreach (var domainId in domainIds)
                {
                    if (nodes.ContainsKey(domainId)) requested.Add((domainId, domainId.ToString()));
                    else result.Skipped.Add(new DiscoverySkipDto { Key = domainId.ToString(), Reason = DiscoverySkipDto.NotADomain });
                }
            }

            // Parents are needed for ancestors and for every row's parent (town) name.
            await LoadAncestorsAsync(nodes);
            var rules = (await _repo.GetRulesAsync()).ToDictionary(r => r.DomainType, StringComparer.OrdinalIgnoreCase);
            var overrides = (await _repo.GetOverridesAsync(nodes.Keys.ToList())).ToDictionary(o => o.DomainId);
            DiscoveryRewardRule? RuleFor(DiscoveryDomainNode node) =>
                rules.TryGetValue(node.DomainType, out var rule)
                    ? DiscoveryRewardCalculator.Merge(rule, overrides.GetValueOrDefault(node.Id))
                    : null;

            // 2. Candidates: what was asked for, then the ancestors of those whose rule includes them.
            var candidates = new Dictionary<int, (DiscoveryDomainNode Node, DiscoverySource Source, string Key)>();
            foreach (var (domainId, key) in requested)
            {
                candidates.TryAdd(domainId, (nodes[domainId], source, key));
            }
            foreach (var (domainId, _) in requested)
            {
                if (RuleFor(nodes[domainId])?.IncludeAncestors != true) continue;
                foreach (var ancestor in Ancestors(nodes[domainId], nodes))
                {
                    candidates.TryAdd(ancestor.Id, (ancestor, DiscoverySource.Ancestor, ancestor.Id.ToString()));
                }
            }

            // 3. Disabled types/overrides are never discovered.
            var enabled = new List<(DiscoveryDomainNode Node, DiscoverySource Source, string Key, DiscoveryRewardRule Rule)>();
            foreach (var candidate in candidates.Values.OrderBy(c => TypeOrder(c.Node.DomainType)).ThenBy(c => c.Node.Id))
            {
                var rule = RuleFor(candidate.Node);
                if (rule == null || !rule.IsEnabled)
                {
                    result.Skipped.Add(new DiscoverySkipDto { Key = candidate.Key, Reason = DiscoverySkipDto.Disabled });
                    continue;
                }
                enabled.Add((candidate.Node, candidate.Source, candidate.Key, rule));
            }

            // 4. Already discovered: nothing new, reported so the plugin stops sending them.
            var discovered = await _repo.GetDiscoveredDomainIdsAsync(userId, enabled.Select(c => c.Node.Id).ToList());
            result.AlreadyDiscovered = enabled.Where(c => discovered.Contains(c.Node.Id)).Select(c => c.Node.Id).ToList();
            var toGrant = enabled.Where(c => !discovered.Contains(c.Node.Id)).ToList();

            // 5. Per-user hourly cap on new discoveries; the excess can be retried later.
            var now = DateTime.UtcNow;
            if (_options.MaxNewPerHour > 0 && toGrant.Count > 0)
            {
                var recent = await _repo.CountSinceAsync(userId, now.AddHours(-1));
                var allowed = Math.Max(0, _options.MaxNewPerHour - recent);
                foreach (var limited in toGrant.Skip(allowed))
                {
                    result.Skipped.Add(new DiscoverySkipDto { Key = limited.Key, Reason = DiscoverySkipDto.RateLimited });
                }
                if (toGrant.Count > allowed) RateLimitedCounter.Add(toGrant.Count - allowed);
                toGrant = toGrant.Take(allowed).ToList();
            }

            result.NewCoins = user.Coins;
            result.NewGems = user.Gems;
            result.NewExperiencePoints = user.ExperiencePoints;
            if (toGrant.Count == 0)
            {
                return result;
            }

            // 6. Rewards: one title bracket and one set of multipliers for the whole request.
            var brackets = await _titleService.GetAllOrderedAsync();
            var bracket = DiscoveryRewardCalculator.CurrentBracket(brackets, user.ExperiencePoints);
            var expUnit = DiscoveryRewardCalculator.ExpUnit(brackets, bracket);
            var salary = DiscoveryRewardCalculator.SalaryOf(bracket);
            var multipliers = CurrencyMultipliersDto.For(user, await _membershipService.GetActiveRankMultipliersAsync(userId));

            var rows = new List<UserDomainDiscovery>();
            foreach (var (node, rowSource, _, rule) in toGrant)
            {
                var roll = DiscoveryRewardCalculator.Roll(rule, expUnit, salary, _random);
                var grant = new DiscoveryGrantDto
                {
                    DomainId = node.Id,
                    WgRegionId = node.WgRegionId,
                    Name = node.Name,
                    DomainType = node.DomainType,
                    ParentName = TownName(node, nodes),
                    Source = rowSource.ToString(),
                    CoinsBase = roll.Coins,
                    GemsBase = roll.Gems,
                    ExpBase = roll.Exp,
                    Coins = CurrencyMultipliersDto.Scale(roll.Coins, multipliers.Coins),
                    Gems = CurrencyMultipliersDto.Scale(roll.Gems, multipliers.Gems),
                    Exp = CurrencyMultipliersDto.Scale(roll.Exp, multipliers.Exp)
                };
                result.Granted.Add(grant);
                rows.Add(new UserDomainDiscovery
                {
                    UserId = userId,
                    DomainId = node.Id,
                    DiscoveredAt = now,
                    Source = rowSource,
                    CoinsAwarded = grant.Coins,
                    GemsAwarded = grant.Gems,
                    ExpAwarded = grant.Exp,
                    TitleBracketId = bracket?.Id,
                    CoinMultiplier = multipliers.Coins,
                    GemMultiplier = multipliers.Gems,
                    ExpMultiplier = multipliers.Exp
                });
            }

            result.TotalCoins = result.Granted.Sum(g => g.Coins);
            result.TotalGems = result.Granted.Sum(g => g.Gems);
            result.TotalExp = result.Granted.Sum(g => g.Exp);
            result.TotalCoinsBase = result.Granted.Sum(g => g.CoinsBase);
            result.TotalGemsBase = result.Granted.Sum(g => g.GemsBase);
            result.TotalExpBase = result.Granted.Sum(g => g.ExpBase);
            result.CoinMultipliers = multipliers.CoinBreakdown;
            result.GemMultipliers = multipliers.GemBreakdown;
            result.ExpMultipliers = multipliers.ExpBreakdown;
            result.TitleBracketId = bracket?.Id;

            // 7. The unique (UserId, DomainId) rows first: a duplicate fails here, before any credit.
            await _repo.AddRangeAsync(rows);

            if (result.TotalCoins != 0 || result.TotalGems != 0 || result.TotalExp != 0)
            {
                // Same scoped DbContext as the rows above, so this commits or rolls back with them.
                // notifyPlayer false: the plugin shows the title change from this response.
                var balances = await _userService.AdjustBalancesAsync(userId, result.TotalCoins, result.TotalGems, result.TotalExp,
                    BalanceReason,
                    JsonSerializer.Serialize(new { domainIds = rows.Select(r => r.DomainId), source = source.ToString() }),
                    actorUserId: null,
                    notifyPlayer: false);
                result.NewCoins = balances.NewCoins;
                result.NewGems = balances.NewGems;
                result.NewExperiencePoints = balances.NewExperiencePoints;
                result.TitleChange = balances.TitleChange;
            }

            foreach (var grant in result.Granted)
            {
                GrantedCounter.Add(1, new KeyValuePair<string, object?>("domain_type", grant.DomainType));
            }
            _logger.LogInformation("Domain discovery: user {UserId} discovered {Count} domain(s) ({DomainIds}) for {Coins} coins, {Gems} gems, {Exp} XP",
                userId, result.Granted.Count, string.Join(",", rows.Select(r => r.DomainId)), result.TotalCoins, result.TotalGems, result.TotalExp);

            return result;
        }

        public async Task<List<KnownDiscoveryDto>> GetKnownAsync(int userId)
        {
            await EnsureUserAsync(userId);
            return (await _repo.GetKnownAsync(userId))
                .Select(k => new KnownDiscoveryDto { DomainId = k.DomainId, WgRegionId = k.WgRegionId })
                .ToList();
        }

        public async Task<PagedResultDto<DiscoveryProgressRowDto>> GetProgressAsync(int userId, PagedQueryDto query)
        {
            await EnsureUserAsync(userId);
            query ??= new PagedQueryDto();

            var rows = await BuildProgressRowsAsync(userId);

            var typeFilter = Filter(query, "domainType");
            if (!string.IsNullOrWhiteSpace(typeFilter))
            {
                rows = rows.Where(r => string.Equals(r.DomainType, typeFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }
            var statusFilter = Filter(query, "status")?.Trim();
            if (string.Equals(statusFilter, "discovered", StringComparison.OrdinalIgnoreCase))
            {
                rows = rows.Where(r => r.Discovered).ToList();
            }
            else if (string.Equals(statusFilter, "undiscovered", StringComparison.OrdinalIgnoreCase))
            {
                rows = rows.Where(r => !r.Discovered).ToList();
            }
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.Trim();
                rows = rows.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            IEnumerable<DiscoveryProgressRowDto> sorted = (query.SortBy ?? "").ToLowerInvariant() switch
            {
                "name" => query.SortDescending
                    ? rows.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    : rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
                "discoveredat" => query.SortDescending
                    ? rows.OrderByDescending(r => r.DiscoveredAt ?? DateTime.MinValue).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    : rows.OrderBy(r => r.DiscoveredAt ?? DateTime.MaxValue).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
                _ => rows.OrderBy(r => TypeOrder(r.DomainType)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.DomainId)
            };

            var pageNumber = Math.Max(1, query.PageNumber);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            return new PagedResultDto<DiscoveryProgressRowDto>
            {
                Items = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = rows.Count,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<DiscoverySummaryDto> GetSummaryAsync(int userId)
        {
            await EnsureUserAsync(userId);
            var rows = await BuildProgressRowsAsync(userId);
            var discoveries = await _repo.GetByUserAsync(userId);

            var latest = rows.Where(r => r.Discovered).OrderByDescending(r => r.DiscoveredAt).ThenByDescending(r => r.DomainId).FirstOrDefault();
            return new DiscoverySummaryDto
            {
                ByType = DiscoveryRewardRule.DomainTypes.Select(type => new DiscoveryTypeCountDto
                {
                    DomainType = type,
                    Discovered = rows.Count(r => r.DomainType == type && r.Discovered),
                    Total = rows.Count(r => r.DomainType == type)
                }).ToList(),
                Latest = latest,
                TotalDiscovered = discoveries.Count,
                TotalCoins = discoveries.Sum(d => d.CoinsAwarded),
                TotalGems = discoveries.Sum(d => d.GemsAwarded),
                TotalExp = discoveries.Sum(d => d.ExpAwarded)
            };
        }

        public async Task<bool> ResetAsync(int userId, int domainId, int? actorUserId)
        {
            await EnsureUserAsync(userId);
            var discovery = await _repo.GetAsync(userId, domainId);
            if (discovery == null) return false;

            var node = (await _repo.GetDomainNodesAsync(new[] { domainId })).FirstOrDefault();
            await _repo.DeleteAsync(discovery);
            // No claw-back (DESIGN.md D8): the reward stays, the domain can be discovered again.
            await _auditLogService.RecordAsync(actorUserId, userId, AuditAction.DiscoveryReset, JsonSerializer.Serialize(new
            {
                domainId,
                domainName = node?.Name,
                domainType = node?.DomainType,
                discoveredAt = DateTime.SpecifyKind(discovery.DiscoveredAt, DateTimeKind.Utc),
                coinsAwarded = discovery.CoinsAwarded,
                gemsAwarded = discovery.GemsAwarded,
                expAwarded = discovery.ExpAwarded
            }));
            return true;
        }

        public async Task<DiscoveryStatsDto> GetStatsAsync(PagedQueryDto query)
        {
            query ??= new PagedQueryDto();
            var (nodes, enabled) = await LoadEnabledNodesAsync();
            var counts = (await _repo.GetDiscovererCountsAsync()).ToDictionary(c => c.DomainId);
            var linkedUsers = await _repo.CountLinkedUsersAsync();

            var stats = enabled.Select(node =>
            {
                var count = counts.GetValueOrDefault(node.Id);
                return new DomainDiscoveryStatDto
                {
                    DomainId = node.Id,
                    Name = node.Name,
                    DomainType = node.DomainType,
                    ParentName = TownName(node, nodes),
                    Discoverers = count?.Discoverers ?? 0,
                    DiscovererPercent = linkedUsers > 0 && count != null
                        ? Math.Round(count.Discoverers * 100m / linkedUsers, 2, MidpointRounding.AwayFromZero)
                        : 0m,
                    FirstDiscoveredAt = count == null ? null : DateTime.SpecifyKind(count.FirstDiscoveredAt, DateTimeKind.Utc)
                };
            }).ToList();

            var typeFilter = Filter(query, "domainType");
            if (!string.IsNullOrWhiteSpace(typeFilter))
            {
                stats = stats.Where(s => string.Equals(s.DomainType, typeFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                stats = stats.Where(s => s.Name.Contains(query.SearchTerm.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }

            IEnumerable<DomainDiscoveryStatDto> sorted = string.Equals(query.SortBy, "name", StringComparison.OrdinalIgnoreCase)
                ? (query.SortDescending ? stats.OrderByDescending(s => s.Name, StringComparer.OrdinalIgnoreCase) : stats.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                : (query.SortDescending ? stats.OrderByDescending(s => s.Discoverers) : stats.OrderBy(s => s.Discoverers))
                    .ThenBy(s => TypeOrder(s.DomainType)).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

            var pageNumber = Math.Max(1, query.PageNumber);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var page = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

            // First discoverer only for the page shown.
            var firsts = (await _repo.GetFirstDiscoveriesAsync(page.Where(s => s.Discoverers > 0).Select(s => s.DomainId).ToList()))
                .ToDictionary(d => d.DomainId);
            var top = await _repo.GetTopExplorersAsync(10);
            var usernames = await _repo.GetUsernamesAsync(firsts.Values.Select(f => f.UserId).Concat(top.Select(t => t.UserId)).ToList());
            foreach (var stat in page)
            {
                if (!firsts.TryGetValue(stat.DomainId, out var first)) continue;
                stat.FirstDiscovererUserId = first.UserId;
                stat.FirstDiscovererUsername = usernames.GetValueOrDefault(first.UserId);
            }

            return new DiscoveryStatsDto
            {
                Domains = new PagedResultDto<DomainDiscoveryStatDto>
                {
                    Items = page,
                    TotalCount = stats.Count,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                },
                LinkedUserCount = linkedUsers,
                TopExplorers = top.Select(t => new DiscoveryExplorerDto
                {
                    UserId = t.UserId,
                    Username = usernames.GetValueOrDefault(t.UserId),
                    Discoveries = t.Discoveries
                }).ToList()
            };
        }

        /// <summary>Every enabled discoverable domain with the user's discovered state, unsorted.</summary>
        private async Task<List<DiscoveryProgressRowDto>> BuildProgressRowsAsync(int userId)
        {
            var (nodes, enabled) = await LoadEnabledNodesAsync();
            var discoveries = (await _repo.GetByUserAsync(userId)).ToDictionary(d => d.DomainId);

            return enabled.Select(node =>
            {
                var found = discoveries.GetValueOrDefault(node.Id);
                return new DiscoveryProgressRowDto
                {
                    DomainId = node.Id,
                    Name = node.Name,
                    DomainType = node.DomainType,
                    ParentName = TownName(node, nodes),
                    Discovered = found != null,
                    DiscoveredAt = found == null ? null : DateTime.SpecifyKind(found.DiscoveredAt, DateTimeKind.Utc),
                    Coins = found?.CoinsAwarded ?? 0,
                    Gems = found?.GemsAwarded ?? 0,
                    Exp = found?.ExpAwarded ?? 0
                };
            }).ToList();
        }

        private async Task<(Dictionary<int, DiscoveryDomainNode> Nodes, List<DiscoveryDomainNode> Enabled)> LoadEnabledNodesAsync()
        {
            var nodes = (await _repo.GetAllDomainNodesAsync()).ToDictionary(n => n.Id);
            var rules = (await _repo.GetRulesAsync()).ToDictionary(r => r.DomainType, StringComparer.OrdinalIgnoreCase);
            var overrides = (await _repo.GetOverridesAsync()).ToDictionary(o => o.DomainId);
            var enabled = nodes.Values
                .Where(n => rules.TryGetValue(n.DomainType, out var rule)
                    && DiscoveryRewardCalculator.Merge(rule, overrides.GetValueOrDefault(n.Id)).IsEnabled)
                .ToList();
            return (nodes, enabled);
        }

        /// <summary>Loads the parents (District, Town) of every node not loaded yet.</summary>
        private async Task LoadAncestorsAsync(Dictionary<int, DiscoveryDomainNode> nodes)
        {
            // Structure -> District -> Town: at most two levels.
            for (var level = 0; level < 2; level++)
            {
                var missing = nodes.Values
                    .Where(n => n.ParentId.HasValue && !nodes.ContainsKey(n.ParentId.Value))
                    .Select(n => n.ParentId!.Value)
                    .Distinct()
                    .ToList();
                if (missing.Count == 0) return;
                foreach (var parent in await _repo.GetDomainNodesAsync(missing))
                {
                    nodes[parent.Id] = parent;
                }
            }
        }

        private static IEnumerable<DiscoveryDomainNode> Ancestors(DiscoveryDomainNode node, IReadOnlyDictionary<int, DiscoveryDomainNode> nodes)
        {
            var current = node;
            for (var depth = 0; depth < 3 && current.ParentId.HasValue && nodes.TryGetValue(current.ParentId.Value, out var parent); depth++)
            {
                yield return parent;
                current = parent;
            }
        }

        /// <summary>The Town a District or Structure lies in (what "in Rivia" names); null for a Town.</summary>
        private static string? TownName(DiscoveryDomainNode node, IReadOnlyDictionary<int, DiscoveryDomainNode> nodes)
        {
            if (node.DomainType == DiscoveryRewardRule.Town) return null;
            return Ancestors(node, nodes).FirstOrDefault(a => a.DomainType == DiscoveryRewardRule.Town)?.Name;
        }

        private static int TypeOrder(string domainType)
        {
            var index = Array.IndexOf(DiscoveryRewardRule.DomainTypes, domainType);
            return index < 0 ? int.MaxValue : index;
        }

        private static DiscoverySource ParseSource(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return DiscoverySource.RegionEnter;
            if (Enum.TryParse<DiscoverySource>(source.Trim(), ignoreCase: true, out var parsed)
                && ClientSources.Contains(parsed)
                && !int.TryParse(source, out _))
            {
                return parsed;
            }
            throw new ArgumentException("source must be RegionEnter, JoinInside or Replay.", nameof(source));
        }

        private async Task EnsureUserAsync(int userId)
        {
            if (userId <= 0 || !await _repo.UserExistsAsync(userId))
            {
                throw new KeyNotFoundException($"User with id {userId} not found.");
            }
        }

        private static string? Filter(PagedQueryDto query, string key)
        {
            if (query.Filters == null) return null;
            foreach (var (k, v) in query.Filters)
            {
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return v;
            }
            return null;
        }

        private static bool IsDuplicateKey(DbUpdateException ex) =>
            ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true;
    }
}
