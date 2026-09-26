namespace knkwebapi_v2.Configuration;

/// <summary>
/// Server-side limits on domain discovery grants (docs/specs/domain-discovery/DESIGN.md §3.4),
/// bound from the "Discovery" section of appsettings.
/// </summary>
public class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    /// <summary>At most this many new discoveries per user in any trailing hour; the rest of a
    /// request is skipped as RateLimited. Bounds farming through the unauthenticated grant
    /// endpoint. 0 or less turns the cap off.</summary>
    public int MaxNewPerHour { get; set; } = 120;
}
