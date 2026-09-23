using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;

namespace knkwebapi_v2.Services
{
    public class PermissionResolutionService : IPermissionResolutionService
    {
        private readonly IUserRepository _userRepo;
        private readonly IPermissionGrantRepository _grantRepo;
        private readonly IPermissionGroupRepository _groupRepo;

        public PermissionResolutionService(
            IUserRepository userRepo,
            IPermissionGrantRepository grantRepo,
            IPermissionGroupRepository groupRepo)
        {
            _userRepo = userRepo;
            _grantRepo = grantRepo;
            _groupRepo = groupRepo;
        }

        /// <summary>
        /// One level in the resolution chain: a holder (the user itself, or one PermissionGroup
        /// in the user's membership/inheritance set) plus its own non-expired grants. Ordered
        /// most-specific-first: the user itself, then each member group (highest Weight first),
        /// then that group's own parent chain immediately after it (before moving to the next
        /// member group) - see PermissionResolutionServiceTests for the exact ordering this
        /// produces and why.
        /// </summary>
        private sealed record HolderLevel(int HolderId, string HolderType, string? HolderName, List<PermissionGrant> Grants);

        private async Task<List<HolderLevel>> BuildHolderChainAsync(User user, DateTime asOf)
        {
            var chain = new List<HolderLevel>();

            var directGrants = await _grantRepo.GetActiveGrantsForHolderAsync(user.Id, asOf);
            chain.Add(new HolderLevel(user.Id, nameof(User), user.Username, directGrants));

            var memberGroups = await _groupRepo.GetActiveGroupsForUserAsync(user.Id, asOf);
            foreach (var group in memberGroups)
            {
                PermissionGroup? current = group;
                var visited = new HashSet<int>();
                while (current != null && visited.Add(current.Id))
                {
                    var activeGrants = (current.Grants ?? new List<PermissionGrant>())
                        .Where(g => g.ExpiresAt == null || g.ExpiresAt > asOf)
                        .ToList();
                    chain.Add(new HolderLevel(current.Id, nameof(PermissionGroup), current.Name, activeGrants));
                    current = current.ParentGroup;
                }
            }

            return chain;
        }

        /// <summary>
        /// Whether grantNode matches queryNode, and if so how specific that match is (higher =
        /// more specific). An exact match always wins; among wildcards, the longer literal prefix
        /// wins - see DESIGN.md §2.2. Wildcard syntax: a trailing "*" matches any node sharing
        /// everything before it, including the node with the trailing ".*"/"*" stripped entirely
        /// (e.g. "knk.gate.*" also matches the bare node "knk.gate"). A bare "*" matches every
        /// node, least specifically.
        /// </summary>
        private static bool TryMatch(string grantNode, string queryNode, out int specificity)
        {
            if (string.Equals(grantNode, queryNode, StringComparison.Ordinal))
            {
                specificity = int.MaxValue;
                return true;
            }

            if (grantNode.EndsWith('*'))
            {
                var prefix = grantNode[..^1];
                if (prefix.Length == 0)
                {
                    specificity = 0;
                    return true;
                }

                var prefixNoTrailingDot = prefix.EndsWith('.') ? prefix[..^1] : prefix;

                if (queryNode.StartsWith(prefix, StringComparison.Ordinal) ||
                    string.Equals(queryNode, prefixNoTrailingDot, StringComparison.Ordinal))
                {
                    specificity = prefix.Length;
                    return true;
                }
            }

            specificity = 0;
            return false;
        }

        /// <summary>
        /// The single best-matching grant at one holder for a node, or null if nothing matches.
        /// Most-specific match wins; an explicit deny beats a grant at the *same* specificity
        /// (DESIGN.md §2.2's "explicit deny... overrides a grant at a less-specific level" -
        /// applied here within one holder's own list, since cross-holder precedence is already
        /// handled by holder chain order in BuildHolderChainAsync/CheckAsync).
        /// </summary>
        private static PermissionGrant? BestMatchAtHolder(List<PermissionGrant> grants, string queryNode)
        {
            var matches = grants
                .Select(g => (Grant: g, Matched: TryMatch(g.Node, queryNode, out var specificity), Specificity: specificity))
                .Where(x => x.Matched)
                .ToList();

            if (matches.Count == 0) return null;

            var maxSpecificity = matches.Max(x => x.Specificity);
            var top = matches.Where(x => x.Specificity == maxSpecificity).Select(x => x.Grant).ToList();

            return top.FirstOrDefault(g => !g.Value) ?? top[0];
        }

        public async Task<PermissionCheckResponseDto?> CheckAsync(int userId, string node)
        {
            if (string.IsNullOrWhiteSpace(node)) throw new ArgumentException("Permission node is required.", nameof(node));

            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return null;

            var chain = await BuildHolderChainAsync(user, DateTime.UtcNow);

            foreach (var level in chain)
            {
                var best = BestMatchAtHolder(level.Grants, node);
                if (best == null) continue;

                return new PermissionCheckResponseDto
                {
                    UserId = userId,
                    Node = node,
                    Result = best.Value ? PermissionResolutionResult.Granted : PermissionResolutionResult.Denied,
                    SourceHolderId = level.HolderId,
                    SourceHolderType = level.HolderType,
                    MatchedNode = best.Node
                };
            }

            return new PermissionCheckResponseDto
            {
                UserId = userId,
                Node = node,
                Result = PermissionResolutionResult.Undeclared
            };
        }

        public async Task<PermissionEffectiveResponseDto?> GetEffectiveAsync(int userId)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return null;

            var chain = await BuildHolderChainAsync(user, DateTime.UtcNow);

            // Every distinct declared node across the whole chain, each resolved to whichever
            // holder wins for that *exact* node string per the same holder-precedence order
            // CheckAsync uses. Wildcard nodes are reported as their own entries (e.g.
            // "knk.gate.*"), not expanded against a fixed node universe that doesn't exist.
            var byNode = new Dictionary<string, EffectivePermissionEntryDto>(StringComparer.Ordinal);

            foreach (var level in chain)
            {
                foreach (var group in level.Grants.GroupBy(g => g.Node, StringComparer.Ordinal))
                {
                    if (byNode.ContainsKey(group.Key)) continue;

                    var chosen = group.FirstOrDefault(g => !g.Value) ?? group.First();
                    byNode[group.Key] = new EffectivePermissionEntryDto
                    {
                        Node = chosen.Node,
                        Value = chosen.Value,
                        SourceHolderId = level.HolderId,
                        SourceHolderType = level.HolderType,
                        SourceHolderName = level.HolderName
                    };
                }
            }

            return new PermissionEffectiveResponseDto
            {
                UserId = userId,
                Permissions = byNode.Values.OrderBy(p => p.Node, StringComparer.Ordinal).ToList()
            };
        }
    }
}
