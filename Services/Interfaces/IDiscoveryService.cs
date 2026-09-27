using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

/// <summary>
/// Domain discovery: first entry into a Town/District/Structure/GateStructure rewards the player
/// once (docs/specs/domain-discovery/DESIGN.md §3.4/§3.5).
/// </summary>
public interface IDiscoveryService
{
    /// <summary>
    /// Records and rewards every not-yet-discovered domain among the request's region/domain ids
    /// (plus ancestors per the type rule), atomically with the balance credit. Idempotent: a repeat
    /// returns the domains as alreadyDiscovered and grants nothing. Throws ArgumentException for an
    /// empty or oversized request or an unknown source, KeyNotFoundException for an unknown user.
    /// </summary>
    Task<DiscoveryGrantResultDto> DiscoverAsync(int userId, DiscoveryGrantRequestDto request);

    /// <summary>Every domain the user has discovered with its region id (plugin cache).</summary>
    Task<List<KnownDiscoveryDto>> GetKnownAsync(int userId);

    /// <summary>Every enabled discoverable domain with the user's discovered state, paged and
    /// filtered by the "domainType" and "status" (discovered/undiscovered) filters and searchTerm.</summary>
    Task<PagedResultDto<DiscoveryProgressRowDto>> GetProgressAsync(int userId, PagedQueryDto query);

    Task<DiscoverySummaryDto> GetSummaryAsync(int userId);

    /// <summary>Deletes one discovery so it can be discovered and rewarded again; nothing is
    /// clawed back. Audited as DiscoveryReset. False when the user hadn't discovered it.</summary>
    Task<bool> ResetAsync(int userId, int domainId, int? actorUserId);

    /// <summary>Discoverers per domain (filter "domainType"; sortBy "discoverers" or "name"),
    /// linked-account count and the top 10 explorers.</summary>
    Task<DiscoveryStatsDto> GetStatsAsync(PagedQueryDto query);
}
