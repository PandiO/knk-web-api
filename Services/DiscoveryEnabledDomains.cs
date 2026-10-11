using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The domains that count as discoverable: their type's reward rule is enabled, after any
    /// per-domain override (docs/specs/domain-discovery/DESIGN.md). One definition for the player's
    /// discovery summary (DiscoveryService) and the KNG-34 discovery statistics and leaderboard, so a
    /// player's /discoveries total and their statistics never disagree. Deleted domains are not
    /// listed either.
    /// </summary>
    public static class DiscoveryEnabledDomains
    {
        public static List<DiscoveryDomainNode> Filter(IEnumerable<DiscoveryDomainNode> nodes, IEnumerable<DiscoveryRewardRule> rules,
            IEnumerable<DomainDiscoveryOverride> overrides)
        {
            var byType = rules.ToDictionary(r => r.DomainType, StringComparer.OrdinalIgnoreCase);
            var byDomain = overrides.ToDictionary(o => o.DomainId);
            return nodes
                .Where(n => byType.TryGetValue(n.DomainType, out var rule)
                    && DiscoveryRewardCalculator.Merge(rule, byDomain.GetValueOrDefault(n.Id)).IsEnabled)
                .ToList();
        }

        /// <summary>Enabled domains by id.</summary>
        public static async Task<Dictionary<int, DiscoveryDomainNode>> LoadAsync(IDiscoveryRepository repo) =>
            Filter(await repo.GetAllDomainNodesAsync(), await repo.GetRulesAsync(), await repo.GetOverridesAsync())
                .ToDictionary(n => n.Id);
    }
}
