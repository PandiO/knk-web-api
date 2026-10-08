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
                    ? ToDto(row, counts.GetValueOrDefault(type))
                    : new DomainNavigationDefaultDto
                    {
                        DomainType = type,
                        DefaultMode = DomainNavigationDefault.Fallback.ToString(),
                        OverrideCount = counts.GetValueOrDefault(type)
                    })
                .ToList();
        }

        public async Task<DomainNavigationDefaultDto> UpdateTypeDefaultAsync(string domainType, UpdateDomainNavigationDefaultDto dto)
        {
            if (dto == null) throw new ArgumentException("A request body is required.", nameof(dto));
            var type = DomainNavigationDefaults.CanonicalType(domainType)
                ?? throw new KeyNotFoundException($"'{domainType}' is not a navigable domain type.");
            var mode = DomainNavigationDefaults.ParseMode(dto.DefaultMode)
                ?? throw new ArgumentException("defaultMode must be Spawn or Region.", nameof(dto));

            var row = await _db.DomainNavigationDefaults.FirstOrDefaultAsync(r => r.DomainType == type);
            if (row == null)
            {
                row = new DomainNavigationDefault { DomainType = type };
                _db.DomainNavigationDefaults.Add(row);
            }
            row.DefaultMode = mode;
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var counts = await OverrideCountsAsync();
            return ToDto(row, counts.GetValueOrDefault(type));
        }

        /// <summary>Domains with an override, per concrete type (Domain is TPT: each row materializes as its subtype).</summary>
        private async Task<Dictionary<string, int>> OverrideCountsAsync()
        {
            var overridden = await _db.Domains.AsNoTracking()
                .Where(d => d.NavigationDefaultOverride != null)
                .ToListAsync();
            return overridden
                .GroupBy(d => d.GetType().Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        }

        private static DomainNavigationDefaultDto ToDto(DomainNavigationDefault row, int overrideCount) => new()
        {
            DomainType = row.DomainType,
            DefaultMode = row.DefaultMode.ToString(),
            OverrideCount = overrideCount,
            UpdatedAt = row.UpdatedAt
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

        /// <summary>Applies the DTO's override: null leaves it, "" / "TypeDefault" clears it, "Spawn" / "Region" set it.</summary>
        public static void Apply(Domain target, IDomainNavigationDefaultDto? dto)
        {
            if (target == null || dto?.NavigationDefaultOverride == null) return;
            var value = dto.NavigationDefaultOverride.Trim();
            if (value.Length == 0 || value.Equals(TypeDefault, StringComparison.OrdinalIgnoreCase))
            {
                target.NavigationDefaultOverride = null;
                return;
            }
            target.NavigationDefaultOverride = ParseMode(value)
                ?? throw new ArgumentException(
                    $"navigationDefaultOverride must be Spawn, Region or empty (the type's default), not '{dto.NavigationDefaultOverride}'.");
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
    }
}
