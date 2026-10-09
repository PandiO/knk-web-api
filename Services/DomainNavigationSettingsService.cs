using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Where <c>/navigate &lt;domain&gt;</c> leads without <c>spawn</c>/<c>region</c> (KNG-73,
    /// docs/specs/navigation/DESIGN.md §6.1): one default per domain type, which a domain's own
    /// <see cref="Domain.NavigationDefaultOverride"/> replaces. The plugin reads the result from
    /// DomainListDto.NavigationDefault (POST api/Domains/search), so it needs no extra call.
    /// </summary>
    public class DomainNavigationSettingsService : IDomainNavigationSettingsService
    {
        private readonly KnKDbContext _db;

        public DomainNavigationSettingsService(KnKDbContext db)
        {
            _db = db;
        }

        public async Task<List<DomainNavigationDefaultDto>> GetTypeDefaultsAsync()
        {
            var rows = (await _db.DomainNavigationDefaults.AsNoTracking().ToListAsync())
                .ToDictionary(r => r.DomainType, StringComparer.OrdinalIgnoreCase);
            var counts = await OverrideCountsAsync();
            return DomainNavigationDefault.DomainTypes
                .Select(type => rows.TryGetValue(type, out var row)
                    ? ToDto(row, counts, stored: true)
                    : ToDto(new DomainNavigationDefault { DomainType = type }, counts, stored: false))
                .ToList();
        }

        public async Task<DomainNavigationDefaultDto> UpdateTypeDefaultAsync(string domainType, UpdateDomainNavigationDefaultDto dto)
        {
            if (dto == null) throw new ArgumentException("A request body is required.", nameof(dto));
            var type = DomainNavigationDefaults.CanonicalType(domainType)
                ?? throw new KeyNotFoundException($"'{domainType}' is not a navigable domain type.");
            if (dto.DefaultMode == null && dto.RoadAccess == null)
                throw new ArgumentException("Give defaultMode, roadAccess or both.", nameof(dto));
            NavigationDestinationMode? mode = dto.DefaultMode == null ? null
                : DomainNavigationDefaults.ParseMode(dto.DefaultMode)
                    ?? throw new ArgumentException("defaultMode must be Spawn or Region.", nameof(dto));
            RoadAccessRule? roadAccess = dto.RoadAccess == null ? null
                : DomainNavigationDefaults.ParseRoadAccess(dto.RoadAccess)
                    ?? throw new ArgumentException("roadAccess must be Applies or Ignored.", nameof(dto));

            var row = await _db.DomainNavigationDefaults.FirstOrDefaultAsync(r => r.DomainType == type);
            if (row == null)
            {
                row = new DomainNavigationDefault { DomainType = type };
                _db.DomainNavigationDefaults.Add(row);
            }
            if (mode.HasValue) row.DefaultMode = mode.Value;
            if (roadAccess.HasValue) row.RoadAccess = roadAccess.Value;
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return ToDto(row, await OverrideCountsAsync(), stored: true);
        }

        private sealed record OverrideCounts(Dictionary<string, int> Mode, Dictionary<string, int> RoadAccess);

        /// <summary>Domains with an override, per concrete type (Domain is TPT: each row materializes as its subtype).</summary>
        private async Task<OverrideCounts> OverrideCountsAsync()
        {
            var overridden = await _db.Domains.AsNoTracking()
                .Where(d => d.NavigationDefaultOverride != null || d.RoadAccessOverride != null)
                .ToListAsync();
            Dictionary<string, int> CountBy(Func<Domain, bool> has) => overridden
                .Where(has)
                .GroupBy(d => d.GetType().Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            return new OverrideCounts(CountBy(d => d.NavigationDefaultOverride != null), CountBy(d => d.RoadAccessOverride != null));
        }

        /// <summary>A type without a row (<paramref name="stored"/> false) shows the fallbacks and no update time.</summary>
        private static DomainNavigationDefaultDto ToDto(DomainNavigationDefault row, OverrideCounts counts, bool stored) => new()
        {
            DomainType = row.DomainType,
            DefaultMode = row.DefaultMode.ToString(),
            OverrideCount = counts.Mode.GetValueOrDefault(row.DomainType),
            RoadAccess = row.RoadAccess.ToString(),
            RoadAccessOverrideCount = counts.RoadAccess.GetValueOrDefault(row.DomainType),
            UpdatedAt = stored ? row.UpdatedAt : null
        };
    }

    /// <summary>
    /// The per-domain <c>/navigate</c> override as carried by the Town/District/Structure/GateStructure
    /// DTOs (KNG-73), and the effective default of a domain. Like DomainTeleportSettings, the override
    /// is never mapped by AutoMapper: services call <see cref="Apply"/>, so a form or a game-server
    /// update without the field leaves it as it is.
    /// </summary>
    public static class DomainNavigationDefaults
    {
        /// <summary>The value that clears an override (besides an empty string).</summary>
        public const string TypeDefault = "TypeDefault";

        /// <summary>
        /// Applies the DTO's overrides - the /navigate default and (rev. 7 Part C) the road access: null leaves
        /// one as it is, "" / "TypeDefault" clears it, a named value sets it. Both are checked before either is written.
        /// </summary>
        public static void Apply(Domain target, IDomainNavigationDefaultDto? dto)
        {
            if (target == null || dto == null) return;
            var mode = Resolve(dto.NavigationDefaultOverride, ParseMode, target.NavigationDefaultOverride,
                $"navigationDefaultOverride must be Spawn, Region or empty (the type's default), not '{dto.NavigationDefaultOverride}'.");
            var roadAccess = Resolve(dto.RoadAccessOverride, ParseRoadAccess, target.RoadAccessOverride,
                $"roadAccessOverride must be Applies, Ignored or empty (the type's default), not '{dto.RoadAccessOverride}'.");
            target.NavigationDefaultOverride = mode;
            target.RoadAccessOverride = roadAccess;
        }

        /// <summary>The override after a form value: null keeps <paramref name="current"/>, "" / "TypeDefault" clears it.</summary>
        private static T? Resolve<T>(string? value, Func<string?, T?> parse, T? current, string error) where T : struct
        {
            if (value == null) return current;
            var trimmed = value.Trim();
            if (trimmed.Length == 0 || trimmed.Equals(TypeDefault, StringComparison.OrdinalIgnoreCase)) return null;
            return parse(trimmed) ?? throw new ArgumentException(error);
        }

        /// <summary>"Spawn" / "Region" in any case; null for anything else.</summary>
        public static NavigationDestinationMode? ParseMode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            foreach (var mode in Enum.GetValues<NavigationDestinationMode>())
            {
                if (mode.ToString().Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) return mode;
            }
            return null;
        }

        /// <summary>"Applies" / "Ignored" in any case; null for anything else.</summary>
        public static RoadAccessRule? ParseRoadAccess(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            foreach (var rule in Enum.GetValues<RoadAccessRule>())
            {
                if (rule.ToString().Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) return rule;
            }
            return null;
        }

        /// <summary>The domain type as stored ("Town", ...), any case in; null for a type that has no default.</summary>
        public static string? CanonicalType(string? domainType) =>
            DomainNavigationDefault.DomainTypes.FirstOrDefault(t => t.Equals(domainType?.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>The domain's own override, else its type's default, else Spawn.</summary>
        public static NavigationDestinationMode Effective(Domain domain, IReadOnlyDictionary<string, NavigationDestinationMode>? typeDefaults)
        {
            if (domain.NavigationDefaultOverride.HasValue) return domain.NavigationDefaultOverride.Value;
            return typeDefaults != null && typeDefaults.TryGetValue(domain.GetType().Name, out var mode)
                ? mode
                : DomainNavigationDefault.Fallback;
        }

        /// <summary>The domain's own road-access override, else its type's, else Applies (rev. 7 Part C).</summary>
        public static RoadAccessRule EffectiveRoadAccess(Domain domain, IReadOnlyDictionary<string, RoadAccessRule>? typeDefaults)
        {
            if (domain.RoadAccessOverride.HasValue) return domain.RoadAccessOverride.Value;
            return typeDefaults != null && typeDefaults.TryGetValue(domain.GetType().Name, out var rule)
                ? rule
                : DomainNavigationDefault.RoadAccessFallback;
        }
    }
}
