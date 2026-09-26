using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class DiscoveryRepository : IDiscoveryRepository
    {
        private readonly KnKDbContext _context;

        public DiscoveryRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<T> RunLockedForUserAsync<T>(int userId, Func<Task<T>> work)
        {
            if (!_context.Database.IsRelational())
            {
                return await work();
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                // Taken before the work reads anything, so a second grant for the same user waits
                // here and then sees the first one's committed discoveries and balances.
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM users WHERE Id = {userId} FOR UPDATE");
                var result = await work();
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public void ResetTracking() => _context.ChangeTracker.Clear();

        public Task<bool> UserExistsAsync(int userId) => _context.Users.AnyAsync(u => u.Id == userId);

        public async Task<List<DiscoveryDomainNode>> FindByWgRegionIdsAsync(IReadOnlyCollection<string> wgRegionIds)
        {
            var lowered = wgRegionIds
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim().ToLower())
                .Distinct()
                .ToList();
            if (lowered.Count == 0) return new List<DiscoveryDomainNode>();

            var ids = await _context.Domains
                .Where(d => d.WgRegionId != null && lowered.Contains(d.WgRegionId.ToLower()))
                .Select(d => d.Id)
                .ToListAsync();
            return await GetDomainNodesAsync(ids);
        }

        public Task<List<DiscoveryDomainNode>> GetDomainNodesAsync(IReadOnlyCollection<int> domainIds)
        {
            var ids = domainIds.Distinct().ToList();
            if (ids.Count == 0) return Task.FromResult(new List<DiscoveryDomainNode>());
            return LoadNodesAsync(ids);
        }

        public Task<List<DiscoveryDomainNode>> GetAllDomainNodesAsync() => LoadNodesAsync(null);

        // Domain is TPT (domains -> towns/districts/structures -> gate_structures). One small
        // projection per subtype keeps the SQL simple on both MySQL and the in-memory provider.
        private async Task<List<DiscoveryDomainNode>> LoadNodesAsync(List<int>? ids)
        {
            var towns = await Filter(_context.Towns, ids)
                .Select(t => new { t.Id, t.Name, t.WgRegionId })
                .ToListAsync();
            var districts = await Filter(_context.Districts, ids)
                .Select(d => new { d.Id, d.Name, d.WgRegionId, d.TownId })
                .ToListAsync();
            var structures = await Filter(_context.Structures, ids)
                .Select(s => new { s.Id, s.Name, s.WgRegionId, s.DistrictId })
                .ToListAsync();
            var gateIds = (await Filter(_context.GateStructures, ids).Select(g => g.Id).ToListAsync()).ToHashSet();

            var nodes = new List<DiscoveryDomainNode>(towns.Count + districts.Count + structures.Count);
            nodes.AddRange(towns.Select(t => new DiscoveryDomainNode(t.Id, t.Name, t.WgRegionId, DiscoveryRewardRule.Town, null)));
            nodes.AddRange(districts.Select(d => new DiscoveryDomainNode(d.Id, d.Name, d.WgRegionId, DiscoveryRewardRule.District,
                d.TownId > 0 ? d.TownId : null)));
            nodes.AddRange(structures.Select(s => new DiscoveryDomainNode(s.Id, s.Name, s.WgRegionId,
                gateIds.Contains(s.Id) ? DiscoveryRewardRule.GateStructure : DiscoveryRewardRule.Structure,
                s.DistrictId > 0 ? s.DistrictId : null)));
            return nodes;
        }

        private static IQueryable<TDomain> Filter<TDomain>(IQueryable<TDomain> source, List<int>? ids) where TDomain : Domain =>
            ids == null ? source : source.Where(d => ids.Contains(d.Id));

        public Task<List<UserDomainDiscovery>> GetByUserAsync(int userId) =>
            _context.UserDomainDiscoveries.AsNoTracking().Where(d => d.UserId == userId).ToListAsync();

        public async Task<List<(int DomainId, string? WgRegionId)>> GetKnownAsync(int userId)
        {
            var rows = await _context.UserDomainDiscoveries
                .Where(d => d.UserId == userId)
                .OrderBy(d => d.DomainId)
                .Select(d => new { d.DomainId, d.Domain.WgRegionId })
                .ToListAsync();
            return rows.Select(r => (r.DomainId, (string?)r.WgRegionId)).ToList();
        }

        public async Task<HashSet<int>> GetDiscoveredDomainIdsAsync(int userId, IReadOnlyCollection<int> domainIds)
        {
            var ids = domainIds.Distinct().ToList();
            if (ids.Count == 0) return new HashSet<int>();
            var found = await _context.UserDomainDiscoveries
                .Where(d => d.UserId == userId && ids.Contains(d.DomainId))
                .Select(d => d.DomainId)
                .ToListAsync();
            return found.ToHashSet();
        }

        public Task<int> CountSinceAsync(int userId, DateTime sinceUtc) =>
            _context.UserDomainDiscoveries.CountAsync(d => d.UserId == userId && d.DiscoveredAt >= sinceUtc);

        public async Task AddRangeAsync(IEnumerable<UserDomainDiscovery> discoveries)
        {
            await _context.UserDomainDiscoveries.AddRangeAsync(discoveries);
            await _context.SaveChangesAsync();
        }

        public Task<UserDomainDiscovery?> GetAsync(int userId, int domainId) =>
            _context.UserDomainDiscoveries.FirstOrDefaultAsync(d => d.UserId == userId && d.DomainId == domainId);

        public async Task DeleteAsync(UserDomainDiscovery discovery)
        {
            _context.UserDomainDiscoveries.Remove(discovery);
            await _context.SaveChangesAsync();
        }

        public Task<List<DiscoveryRewardRule>> GetRulesAsync() => _context.DiscoveryRewardRules.ToListAsync();

        public Task<DiscoveryRewardRule?> GetRuleAsync(string domainType) =>
            _context.DiscoveryRewardRules.FirstOrDefaultAsync(r => r.DomainType == domainType);

        public async Task<DiscoveryRewardRule> UpsertRuleAsync(DiscoveryRewardRule rule)
        {
            if (_context.Entry(rule).State == EntityState.Detached)
            {
                var exists = await _context.DiscoveryRewardRules.AnyAsync(r => r.DomainType == rule.DomainType);
                if (exists) _context.DiscoveryRewardRules.Update(rule);
                else await _context.DiscoveryRewardRules.AddAsync(rule);
            }
            await _context.SaveChangesAsync();
            return rule;
        }

        public Task<List<DomainDiscoveryOverride>> GetOverridesAsync() =>
            _context.DomainDiscoveryOverrides.OrderBy(o => o.DomainId).ToListAsync();

        public Task<List<DomainDiscoveryOverride>> GetOverridesAsync(IReadOnlyCollection<int> domainIds)
        {
            var ids = domainIds.Distinct().ToList();
            return _context.DomainDiscoveryOverrides.Where(o => ids.Contains(o.DomainId)).ToListAsync();
        }

        public Task<DomainDiscoveryOverride?> GetOverrideAsync(int domainId) =>
            _context.DomainDiscoveryOverrides.FirstOrDefaultAsync(o => o.DomainId == domainId);

        public async Task<DomainDiscoveryOverride> UpsertOverrideAsync(DomainDiscoveryOverride domainOverride)
        {
            if (_context.Entry(domainOverride).State == EntityState.Detached)
            {
                var exists = await _context.DomainDiscoveryOverrides.AnyAsync(o => o.DomainId == domainOverride.DomainId);
                if (exists) _context.DomainDiscoveryOverrides.Update(domainOverride);
                else await _context.DomainDiscoveryOverrides.AddAsync(domainOverride);
            }
            await _context.SaveChangesAsync();
            return domainOverride;
        }

        public async Task DeleteOverrideAsync(DomainDiscoveryOverride domainOverride)
        {
            _context.DomainDiscoveryOverrides.Remove(domainOverride);
            await _context.SaveChangesAsync();
        }

        public async Task<List<DomainDiscovererCount>> GetDiscovererCountsAsync()
        {
            var rows = await _context.UserDomainDiscoveries
                .GroupBy(d => d.DomainId)
                .Select(g => new { DomainId = g.Key, Count = g.Count(), First = g.Min(d => d.DiscoveredAt) })
                .ToListAsync();
            return rows.Select(r => new DomainDiscovererCount(r.DomainId, r.Count, r.First)).ToList();
        }

        public async Task<List<UserDomainDiscovery>> GetFirstDiscoveriesAsync(IReadOnlyCollection<int> domainIds)
        {
            var ids = domainIds.Distinct().ToList();
            if (ids.Count == 0) return new List<UserDomainDiscovery>();
            // Only called for one page of domains, so reading their rows and picking the earliest
            // in memory is cheaper to reason about than a correlated subquery per provider.
            var rows = await _context.UserDomainDiscoveries.AsNoTracking()
                .Where(d => ids.Contains(d.DomainId))
                .OrderBy(d => d.DiscoveredAt).ThenBy(d => d.Id)
                .ToListAsync();
            return rows.GroupBy(d => d.DomainId).Select(g => g.First()).ToList();
        }

        public async Task<List<(int UserId, int Discoveries)>> GetTopExplorersAsync(int count)
        {
            var rows = await _context.UserDomainDiscoveries
                .GroupBy(d => d.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .OrderByDescending(r => r.Count).ThenBy(r => r.UserId)
                .Take(count)
                .ToListAsync();
            return rows.Select(r => (r.UserId, r.Count)).ToList();
        }

        public Task<int> CountLinkedUsersAsync() =>
            _context.Users.CountAsync(u => u.IsActive && u.Uuid != null && u.Uuid != "");

        public async Task<Dictionary<int, string>> GetUsernamesAsync(IReadOnlyCollection<int> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, string>();
            return await _context.Users
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.Username })
                .ToDictionaryAsync(u => u.Id, u => u.Username);
        }
    }
}
