using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>
    /// A discoverable domain reduced to what discovery needs: its concrete type (Town, District,
    /// Structure, GateStructure) and its parent (a District's Town, a Structure's District).
    /// </summary>
    public record DiscoveryDomainNode(int Id, string Name, string? WgRegionId, string DomainType, int? ParentId);

    /// <summary>Discoverer count and first discovery time of one domain.</summary>
    public record DomainDiscovererCount(int DomainId, int Discoverers, DateTime FirstDiscoveredAt);

    /// <summary>Data access for domain discovery (docs/specs/domain-discovery/DESIGN.md §3.1/§3.4).</summary>
    public interface IDiscoveryRepository
    {
        /// <summary>
        /// Runs <paramref name="work"/> in a transaction holding a row lock on the user
        /// (SELECT ... FOR UPDATE), so concurrent grants for one user run one after another and the
        /// discovery rows and the balance credit commit or roll back together. On a non-relational
        /// provider (the in-memory test database) the work just runs.
        /// </summary>
        Task<T> RunLockedForUserAsync<T>(int userId, Func<Task<T>> work);

        /// <summary>Forgets every tracked entity (after a failed save, before retrying).</summary>
        void ResetTracking();

        Task<bool> UserExistsAsync(int userId);

        /// <summary>Domains whose WgRegionId matches one of <paramref name="wgRegionIds"/>, ignoring case.
        /// Only Towns, Districts, Structures and GateStructures are returned.</summary>
        Task<List<DiscoveryDomainNode>> FindByWgRegionIdsAsync(IReadOnlyCollection<string> wgRegionIds);

        Task<List<DiscoveryDomainNode>> GetDomainNodesAsync(IReadOnlyCollection<int> domainIds);

        Task<List<DiscoveryDomainNode>> GetAllDomainNodesAsync();

        Task<List<UserDomainDiscovery>> GetByUserAsync(int userId);

        Task<List<(int DomainId, string? WgRegionId)>> GetKnownAsync(int userId);

        Task<HashSet<int>> GetDiscoveredDomainIdsAsync(int userId, IReadOnlyCollection<int> domainIds);

        /// <summary>How many domains the user discovered at or after <paramref name="sinceUtc"/>.</summary>
        Task<int> CountSinceAsync(int userId, DateTime sinceUtc);

        /// <summary>Adds the rows and saves. Throws DbUpdateException on a (UserId, DomainId) duplicate.</summary>
        Task AddRangeAsync(IEnumerable<UserDomainDiscovery> discoveries);

        Task<UserDomainDiscovery?> GetAsync(int userId, int domainId);

        Task DeleteAsync(UserDomainDiscovery discovery);

        Task<List<DiscoveryRewardRule>> GetRulesAsync();

        Task<DiscoveryRewardRule?> GetRuleAsync(string domainType);

        Task<DiscoveryRewardRule> UpsertRuleAsync(DiscoveryRewardRule rule);

        Task<List<DomainDiscoveryOverride>> GetOverridesAsync();

        Task<List<DomainDiscoveryOverride>> GetOverridesAsync(IReadOnlyCollection<int> domainIds);

        Task<DomainDiscoveryOverride?> GetOverrideAsync(int domainId);

        Task<DomainDiscoveryOverride> UpsertOverrideAsync(DomainDiscoveryOverride domainOverride);

        Task DeleteOverrideAsync(DomainDiscoveryOverride domainOverride);

        /// <summary>Discoverer count and first discovery per discovered domain.</summary>
        Task<List<DomainDiscovererCount>> GetDiscovererCountsAsync();

        /// <summary>The earliest discovery row of each of <paramref name="domainIds"/>.</summary>
        Task<List<UserDomainDiscovery>> GetFirstDiscoveriesAsync(IReadOnlyCollection<int> domainIds);

        /// <summary>The <paramref name="count"/> users with the most discoveries.</summary>
        Task<List<(int UserId, int Discoveries)>> GetTopExplorersAsync(int count);

        /// <summary>Active users with a linked Minecraft account (a UUID).</summary>
        Task<int> CountLinkedUsersAsync();

        Task<Dictionary<int, string>> GetUsernamesAsync(IReadOnlyCollection<int> userIds);
    }
}
