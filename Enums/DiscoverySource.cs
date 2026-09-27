namespace knkwebapi_v2.Enums;

/// <summary>
/// How a <see cref="knkwebapi_v2.Models.UserDomainDiscovery"/> came about
/// (docs/specs/domain-discovery/DESIGN.md §3.1). Stored as its string name, like AuditAction.
/// </summary>
public enum DiscoverySource
{
    /// <summary>The player walked or teleported into the domain's region.</summary>
    RegionEnter = 0,

    /// <summary>The player was already standing inside the region when they joined.</summary>
    JoinInside = 1,

    /// <summary>Discovered together with a child domain (entering a District discovers its Town).</summary>
    Ancestor = 2,

    /// <summary>Replayed by the plugin from its spool after the API was unreachable.</summary>
    Replay = 3,

    /// <summary>Granted by staff.</summary>
    Admin = 4
}
