using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Discovery reward rules, per-domain overrides and the per-title preview
    /// (docs/specs/domain-discovery/DESIGN.md §3.1/§3.5). Web-app admin only.
    /// </summary>
    public class DiscoveryConfigurationService : IDiscoveryConfigurationService
    {
        /// <summary>Upper bound on XP units and salary hours - far above any sane reward, low enough
        /// that a typo can't mint billions of coins.</summary>
        public const decimal MaxUnits = 1000m;

        /// <summary>Upper bound on flat gems per discovery.</summary>
        public const int MaxGems = 100_000;

        private readonly IDiscoveryRepository _repo;
        private readonly ITitleService _titleService;

        public DiscoveryConfigurationService(IDiscoveryRepository repo, ITitleService titleService)
        {
            _repo = repo;
            _titleService = titleService;
        }

        public async Task<List<DiscoveryRewardRuleDto>> GetRulesAsync()
        {
            var rules = (await _repo.GetRulesAsync()).ToDictionary(r => r.DomainType, StringComparer.OrdinalIgnoreCase);
            // A type whose row is missing reads as disabled, which is also how grants treat it.
            return DiscoveryRewardRule.DomainTypes
                .Select(type => rules.TryGetValue(type, out var rule) ? ToDto(rule) : new DiscoveryRewardRuleDto { DomainType = type })
                .ToList();
        }

        public async Task<DiscoveryRewardRuleDto> UpdateRuleAsync(string domainType, UpdateDiscoveryRewardRuleDto dto)
        {
            if (dto == null) throw new ArgumentException("A request body is required.", nameof(dto));
            var type = CanonicalType(domainType)
                ?? throw new KeyNotFoundException($"'{domainType}' is not a discoverable domain type.");

            Validate(dto.ExpUnitsMin, dto.ExpUnitsMax, dto.CoinSalaryHoursMin, dto.CoinSalaryHoursMax, dto.GemsMin, dto.GemsMax);

            var rule = await _repo.GetRuleAsync(type) ?? new DiscoveryRewardRule { DomainType = type };
            rule.IsEnabled = dto.IsEnabled;
            rule.ExpUnitsMin = dto.ExpUnitsMin;
            rule.ExpUnitsMax = dto.ExpUnitsMax;
            rule.CoinSalaryHoursMin = dto.CoinSalaryHoursMin;
            rule.CoinSalaryHoursMax = dto.CoinSalaryHoursMax;
            rule.GemsMin = dto.GemsMin;
            rule.GemsMax = dto.GemsMax;
            rule.IncludeAncestors = dto.IncludeAncestors;
            rule.UpdatedAt = DateTime.UtcNow;
            return ToDto(await _repo.UpsertRuleAsync(rule));
        }

        public async Task<List<DomainDiscoveryOverrideDto>> GetOverridesAsync()
        {
            var overrides = await _repo.GetOverridesAsync();
            var nodes = (await _repo.GetDomainNodesAsync(overrides.Select(o => o.DomainId).ToList())).ToDictionary(n => n.Id);
            return overrides.Select(o => ToDto(o, nodes.GetValueOrDefault(o.DomainId))).ToList();
        }

        public async Task<DomainDiscoveryOverrideDto> UpsertOverrideAsync(int domainId, UpdateDomainDiscoveryOverrideDto dto)
        {
            if (dto == null) throw new ArgumentException("A request body is required.", nameof(dto));
            var node = (await _repo.GetDomainNodesAsync(new[] { domainId })).FirstOrDefault()
                ?? throw new KeyNotFoundException($"Domain {domainId} is not a discoverable domain.");

            var domainOverride = await _repo.GetOverrideAsync(domainId) ?? new DomainDiscoveryOverride { DomainId = domainId };
            domainOverride.IsEnabled = dto.IsEnabled;
            domainOverride.ExpUnitsMin = dto.ExpUnitsMin;
            domainOverride.ExpUnitsMax = dto.ExpUnitsMax;
            domainOverride.CoinSalaryHoursMin = dto.CoinSalaryHoursMin;
            domainOverride.CoinSalaryHoursMax = dto.CoinSalaryHoursMax;
            domainOverride.GemsMin = dto.GemsMin;
            domainOverride.GemsMax = dto.GemsMax;
            domainOverride.IncludeAncestors = dto.IncludeAncestors;
            domainOverride.UpdatedAt = DateTime.UtcNow;

            // Validate what grants would actually use: the override merged onto the type rule.
            var rule = await _repo.GetRuleAsync(node.DomainType) ?? new DiscoveryRewardRule { DomainType = node.DomainType };
            var merged = DiscoveryRewardCalculator.Merge(rule, domainOverride);
            Validate(merged.ExpUnitsMin, merged.ExpUnitsMax, merged.CoinSalaryHoursMin, merged.CoinSalaryHoursMax, merged.GemsMin, merged.GemsMax);

            return ToDto(await _repo.UpsertOverrideAsync(domainOverride), node);
        }

        public async Task<bool> DeleteOverrideAsync(int domainId)
        {
            var domainOverride = await _repo.GetOverrideAsync(domainId);
            if (domainOverride == null) return false;
            await _repo.DeleteOverrideAsync(domainOverride);
            return true;
        }

        public async Task<DiscoveryRewardPreviewDto> PreviewAsync(string? domainType, int? domainId)
        {
            string type;
            DomainDiscoveryOverride? domainOverride = null;
            if (domainId.HasValue)
            {
                var node = (await _repo.GetDomainNodesAsync(new[] { domainId.Value })).FirstOrDefault()
                    ?? throw new KeyNotFoundException($"Domain {domainId} is not a discoverable domain.");
                type = node.DomainType;
                domainOverride = await _repo.GetOverrideAsync(domainId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(domainType))
            {
                type = CanonicalType(domainType)
                    ?? throw new KeyNotFoundException($"'{domainType}' is not a discoverable domain type.");
            }
            else
            {
                throw new ArgumentException("Give a domainType or a domainId.");
            }

            var rule = await _repo.GetRuleAsync(type) ?? new DiscoveryRewardRule { DomainType = type, IsEnabled = false };
            var effective = DiscoveryRewardCalculator.Merge(rule, domainOverride);
            var brackets = await _titleService.GetAllOrderedAsync();

            return new DiscoveryRewardPreviewDto
            {
                DomainType = type,
                DomainId = domainId,
                Rule = ToDto(effective),
                Rows = brackets.Select(bracket =>
                {
                    var unit = DiscoveryRewardCalculator.ExpUnit(brackets, bracket);
                    var salary = DiscoveryRewardCalculator.SalaryOf(bracket);
                    var min = DiscoveryRewardCalculator.Min(effective, unit, salary);
                    var max = DiscoveryRewardCalculator.Max(effective, unit, salary);
                    return new DiscoveryRewardPreviewRowDto
                    {
                        TitleBracketId = bracket.Id,
                        TitleName = bracket.MaleName,
                        MinExperience = bracket.MinExperience,
                        ExpUnit = unit,
                        Salary = salary,
                        ExpMin = min.Exp,
                        ExpMax = max.Exp,
                        CoinsMin = min.Coins,
                        CoinsMax = max.Coins,
                        GemsMin = min.Gems,
                        GemsMax = max.Gems
                    };
                }).ToList()
            };
        }

        /// <summary>The canonical spelling of a discoverable domain type, or null.</summary>
        public static string? CanonicalType(string? domainType) =>
            DiscoveryRewardRule.DomainTypes.FirstOrDefault(t => string.Equals(t, domainType?.Trim(), StringComparison.OrdinalIgnoreCase));

        private static void Validate(decimal expMin, decimal expMax, decimal coinMin, decimal coinMax, int gemsMin, int gemsMax)
        {
            ValidateRange("expUnits", expMin, expMax, MaxUnits);
            ValidateRange("coinSalaryHours", coinMin, coinMax, MaxUnits);
            ValidateRange("gems", gemsMin, gemsMax, MaxGems);
        }

        private static void ValidateRange(string name, decimal min, decimal max, decimal limit)
        {
            if (min < 0 || max < 0) throw new ArgumentException($"{name}Min and {name}Max cannot be negative.");
            if (min > limit || max > limit) throw new ArgumentException($"{name}Min and {name}Max cannot exceed {limit}.");
            if (min > max) throw new ArgumentException($"{name}Min ({min}) cannot exceed {name}Max ({max}).");
        }

        private static DiscoveryRewardRuleDto ToDto(DiscoveryRewardRule rule) => new()
        {
            DomainType = rule.DomainType,
            IsEnabled = rule.IsEnabled,
            ExpUnitsMin = rule.ExpUnitsMin,
            ExpUnitsMax = rule.ExpUnitsMax,
            CoinSalaryHoursMin = rule.CoinSalaryHoursMin,
            CoinSalaryHoursMax = rule.CoinSalaryHoursMax,
            GemsMin = rule.GemsMin,
            GemsMax = rule.GemsMax,
            IncludeAncestors = rule.IncludeAncestors,
            UpdatedAt = DateTime.SpecifyKind(rule.UpdatedAt, DateTimeKind.Utc)
        };

        private static DomainDiscoveryOverrideDto ToDto(DomainDiscoveryOverride o, DiscoveryDomainNode? node) => new()
        {
            DomainId = o.DomainId,
            DomainName = node?.Name,
            DomainType = node?.DomainType,
            IsEnabled = o.IsEnabled,
            ExpUnitsMin = o.ExpUnitsMin,
            ExpUnitsMax = o.ExpUnitsMax,
            CoinSalaryHoursMin = o.CoinSalaryHoursMin,
            CoinSalaryHoursMax = o.CoinSalaryHoursMax,
            GemsMin = o.GemsMin,
            GemsMax = o.GemsMax,
            IncludeAncestors = o.IncludeAncestors,
            UpdatedAt = DateTime.SpecifyKind(o.UpdatedAt, DateTimeKind.Utc)
        };
    }
}
