using System.Collections.Concurrent;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The read checks of the dashboard access model (PandiO/knk-workspace#280 AM2, knk-workspace
    /// docs/specs/dashboard-access-model/DESIGN.md § 4.6.5): "does this user hold any of these nodes", answered from a
    /// per-request memo and a 30 s per-(user, node) cache, like <c>RealtimeWebAudience</c>. For <b>reads only</b>
    /// (<c>DashboardReads</c>, the any-area-node rules); writes keep their uncached checks.
    /// <para>The grant, membership and group services drop entries when they change (<see cref="ForgetUser"/>,
    /// <see cref="ForgetAll"/>), at the same calls that publish S2's <c>UserPermissionsChanged</c> /
    /// <c>PermissionGroupChanged</c>; any other change (an expiring grant, a database edit) shows within 30 s.</para>
    /// </summary>
    public interface IPermissionReadCache
    {
        Task<bool> HoldsAnyAsync(IPermissionResolutionService permissions, int userId, IReadOnlyList<string> nodes, HttpContext? http = null);

        /// <summary>A user's grants or memberships changed.</summary>
        void ForgetUser(int userId);

        /// <summary>A group (its grants, parent or weight) changed, which reaches every member: drop everything.</summary>
        void ForgetAll();
    }

    /// <summary>Singleton. A failed check is not cached.</summary>
    public sealed class PermissionReadCache : IPermissionReadCache
    {
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

        private const string RequestMemoKey = "knk.readPermissionMemo";

        private readonly ConcurrentDictionary<(int UserId, string Node), (bool Allowed, long ExpiresTicks)> _cache = new();
        private readonly TimeProvider _time;
        private readonly TimeSpan _ttl;
        private long _generation;

        public PermissionReadCache() : this(null, null)
        {
        }

        public PermissionReadCache(TimeProvider? time, TimeSpan? ttl = null)
        {
            _time = time ?? TimeProvider.System;
            _ttl = ttl ?? DefaultTtl;
        }

        public async Task<bool> HoldsAnyAsync(IPermissionResolutionService permissions, int userId, IReadOnlyList<string> nodes,
            HttpContext? http = null)
        {
            if (userId <= 0 || nodes.Count == 0) return false;

            var memo = Memo(http);
            var now = _time.GetUtcNow().UtcTicks;
            var unknown = new List<string>();
            foreach (var node in nodes)
            {
                if (memo != null && memo.TryGetValue((userId, node), out var memoised))
                {
                    if (memoised) return true;
                    continue;
                }
                if (_cache.TryGetValue((userId, node), out var cached) && cached.ExpiresTicks > now)
                {
                    if (cached.Allowed) return true;
                    continue;
                }
                unknown.Add(node);
            }
            if (unknown.Count == 0) return false;

            var generation = Interlocked.Read(ref _generation);
            var granted = await permissions.CheckAnyAsync(userId, unknown);

            // Nodes before the granted one are refused; those after it weren't looked at.
            var answers = new List<(string Node, bool Allowed)>();
            foreach (var node in unknown)
            {
                var allowed = string.Equals(node, granted, StringComparison.Ordinal);
                answers.Add((node, allowed));
                if (allowed) break;
            }

            foreach (var (node, allowed) in answers)
            {
                if (memo != null) memo[(userId, node)] = allowed;
                // A Forget that ran while we were checking wins: don't store an answer from before it.
                if (_ttl > TimeSpan.Zero && Interlocked.Read(ref _generation) == generation)
                {
                    _cache[(userId, node)] = (allowed, now + _ttl.Ticks);
                }
            }
            return granted != null;
        }

        public void ForgetUser(int userId)
        {
            Interlocked.Increment(ref _generation);
            foreach (var key in _cache.Keys)
            {
                if (key.UserId == userId) _cache.TryRemove(key, out _);
            }
        }

        public void ForgetAll()
        {
            Interlocked.Increment(ref _generation);
            _cache.Clear();
        }

        private static Dictionary<(int, string), bool>? Memo(HttpContext? http)
        {
            if (http == null) return null;
            if (http.Items.TryGetValue(RequestMemoKey, out var existing) && existing is Dictionary<(int, string), bool> memo) return memo;
            memo = new Dictionary<(int, string), bool>();
            http.Items[RequestMemoKey] = memo;
            return memo;
        }
    }
}
