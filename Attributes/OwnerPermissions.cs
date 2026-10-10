namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// Owner-only permission nodes (KNG-34 D12, D24, DESIGN.md §F.13). Never granted by any seed or
    /// migration: the owner grants them to themselves (POST api/users/{id}/grants). Enforced with
    /// <see cref="RequireOwnerPermissionAttribute"/>. The nodes in <see cref="ExactGrantOnly"/>
    /// (personal diagnostic data, GDPR deletion) need an exact grant, because the wildcard resolver
    /// lets <c>*</c> and <c>knk.*</c> match <c>knk.owner.*</c>; the others resolve like any node.
    /// </summary>
    public static class OwnerPermissions
    {
        public const string Prefix = "knk.owner.";

        /// <summary>Diagnostic timeline search and event details (link 6).</summary>
        public const string TelemetryView = "knk.owner.telemetry.view";

        /// <summary>Test runs, enhanced-mode targets, statistics rebuilds (link 6).</summary>
        public const string TelemetryManage = "knk.owner.telemetry.manage";

        /// <summary>GDPR deletion requests and execution (link 6).</summary>
        public const string PrivacyManage = "knk.owner.privacy.manage";

        /// <summary>World analytics: heatmaps, menu funnels, domain interactions (link 7).</summary>
        public const string AnalyticsView = "knk.owner.analytics.view";

        /// <summary>Leaderboard exclusions (link 5).</summary>
        public const string LeaderboardManage = "knk.owner.leaderboard.manage";

        /// <summary>
        /// Owner nodes a wildcard never unlocks (D24): they reach per-player diagnostic data or run
        /// irreversible GDPR deletions. World analytics (anonymous) and leaderboard exclusions also
        /// accept <c>*</c>, <c>knk.*</c> and <c>knk.owner.*</c>.
        /// </summary>
        public static readonly IReadOnlySet<string> ExactGrantOnly =
            new HashSet<string>(StringComparer.Ordinal) { TelemetryView, TelemetryManage, PrivacyManage };
    }
}
