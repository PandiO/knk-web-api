using System.Threading.Tasks;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Services.Statistics
{
    public enum StatisticsViewerKind
    {
        /// <summary>The player themselves: sees everything except internal metrics.</summary>
        Self,

        /// <summary>Staff holding knk.admin.statistics.view (moderation view, L1-20).</summary>
        Staff,

        /// <summary>Another signed-in player (web JWT, or the plugin acting for an online player).</summary>
        SignedIn,

        /// <summary>Nobody signed in: always-public fields only (L1-3).</summary>
        Anonymous
    }

    public sealed record StatisticsViewer(StatisticsViewerKind Kind, int? UserId)
    {
        public static StatisticsViewer Anonymous { get; } = new(StatisticsViewerKind.Anonymous, null);

        /// <summary>Self and staff see every non-internal statistic regardless of the settings.</summary>
        public bool SeesEverything => Kind is StatisticsViewerKind.Self or StatisticsViewerKind.Staff;

        /// <summary>The DTO's "viewer" value.</summary>
        public string Name => Kind switch
        {
            StatisticsViewerKind.Self => "self",
            StatisticsViewerKind.Staff => "staff",
            StatisticsViewerKind.SignedIn => "signedIn",
            _ => "anonymous"
        };
    }

    public interface IStatisticsViewerResolver
    {
        /// <summary>Who is looking at <paramref name="targetUserId"/>'s statistics.</summary>
        Task<StatisticsViewer> ResolveAsync(KnkCaller caller, int targetUserId);
    }

    /// <summary>
    /// Resolves the statistics viewer (IMPLEMENTATION_PLAN.md §3): the JWT user, or — for a plugin
    /// request — the online player in X-Acting-User-Id; a plugin request without one, and every
    /// other caller, is anonymous. Staff = the normal (wildcard-aware) resolution of
    /// knk.admin.statistics.view.
    /// </summary>
    public class StatisticsViewerResolver : IStatisticsViewerResolver
    {
        private readonly IPermissionResolutionService _permissions;

        public StatisticsViewerResolver(IPermissionResolutionService permissions)
        {
            _permissions = permissions;
        }

        public async Task<StatisticsViewer> ResolveAsync(KnkCaller caller, int targetUserId)
        {
            var viewerId = caller.IsWebUser ? caller.WebUserId : caller.IsPluginService ? caller.ActingUserId : null;
            if (viewerId == null) return StatisticsViewer.Anonymous;
            if (viewerId == targetUserId) return new StatisticsViewer(StatisticsViewerKind.Self, viewerId);

            var check = await _permissions.CheckAsync(viewerId.Value, StaffPermissions.ViewStatistics);
            return check?.Allowed == true
                ? new StatisticsViewer(StatisticsViewerKind.Staff, viewerId)
                : new StatisticsViewer(StatisticsViewerKind.SignedIn, viewerId);
        }
    }
}
