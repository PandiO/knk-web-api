using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

/// <summary>
/// Admin configuration of discovery rewards: per-type rules, per-domain overrides and the
/// per-title preview (docs/specs/domain-discovery/DESIGN.md §3.1/§3.5).
/// </summary>
public interface IDiscoveryConfigurationService
{
    /// <summary>The four type rules, top-down (Town, District, Structure, GateStructure).</summary>
    Task<List<DiscoveryRewardRuleDto>> GetRulesAsync();

    /// <summary>Replaces a type's rule. KeyNotFoundException for a type that isn't discoverable,
    /// ArgumentException when a value is negative, too large, or a min exceeds its max.</summary>
    Task<DiscoveryRewardRuleDto> UpdateRuleAsync(string domainType, UpdateDiscoveryRewardRuleDto dto);

    Task<List<DomainDiscoveryOverrideDto>> GetOverridesAsync();

    /// <summary>Creates or replaces a domain's override. KeyNotFoundException when the domain
    /// isn't a discoverable domain, ArgumentException when the values (merged with the type rule)
    /// are invalid.</summary>
    Task<DomainDiscoveryOverrideDto> UpsertOverrideAsync(int domainId, UpdateDomainDiscoveryOverrideDto dto);

    /// <summary>False when the domain had no override.</summary>
    Task<bool> DeleteOverrideAsync(int domainId);

    /// <summary>What a type (or one domain, override applied) pays at each title at multiplier
    /// 1.0. The domain's own type wins over <paramref name="domainType"/> when both are given.</summary>
    Task<DiscoveryRewardPreviewDto> PreviewAsync(string? domainType, int? domainId);
}
