using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Fills <see cref="Domain.WorldName"/> on domains created before KNG-111. The migration already copied the world of
    /// each domain's Location and its parent; this pass also uses the game server's report of which world(s) hold each
    /// region. A domain is only filled when its sources agree; the rest are listed for an admin to choose in the form.
    /// </summary>
    public interface IDomainWorldBackfill
    {
        Task<DomainWorldBackfillResultDto> ApplyAsync(DomainWorldBackfillRequestDto request);

        Task<List<DomainWorldMissingDto>> ListMissingAsync();
    }

    public class DomainWorldBackfill : IDomainWorldBackfill
    {
        private readonly KnKDbContext _context;
        private readonly ILogger<DomainWorldBackfill> _logger;

        public DomainWorldBackfill(KnKDbContext context, ILogger<DomainWorldBackfill> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<DomainWorldMissingDto>> ListMissingAsync()
        {
            var missing = await _context.Domains.AsNoTracking()
                .Where(d => d.WorldName == null || d.WorldName == "")
                .OrderBy(d => d.Id)
                .ToListAsync();
            return missing.Select(d => Missing(d, new List<string>())).ToList();
        }

        public async Task<DomainWorldBackfillResultDto> ApplyAsync(DomainWorldBackfillRequestDto request)
        {
            var reported = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var region in request?.Regions ?? new List<DomainRegionWorldsDto>())
            {
                if (string.IsNullOrWhiteSpace(region?.WgRegionId)) continue;
                var worlds = (region.Worlds ?? new List<string>())
                    .Where(w => !string.IsNullOrWhiteSpace(w))
                    .Select(w => w.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                reported[region.WgRegionId.Trim()] = worlds;
            }

            var domains = await _context.Domains.ToListAsync();
            var byId = domains.ToDictionary(d => d.Id);
            var locationIds = domains
                .Where(d => IsBlank(d.WorldName) && d.LocationId.HasValue)
                .Select(d => d.LocationId!.Value)
                .ToList();
            var locationWorlds = await _context.Locations.AsNoTracking()
                .Where(l => locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.World);

            int updated = 0;
            bool changed = true;
            // Parents first fill their children, so repeat until a pass changes nothing (at most Town → District → Structure).
            while (changed)
            {
                changed = false;
                foreach (var domain in domains.Where(d => IsBlank(d.WorldName)).OrderBy(d => d.Id))
                {
                    string? world = Decide(domain, byId, reported, locationWorlds);
                    if (world == null) continue;
                    domain.WorldName = world;
                    updated++;
                    changed = true;
                }
            }

            if (updated > 0)
            {
                await _context.SaveChangesAsync();
            }

            var unresolved = domains
                .Where(d => IsBlank(d.WorldName))
                .OrderBy(d => d.Id)
                .Select(d => Missing(d, Candidates(d, reported)))
                .ToList();
            _logger.LogInformation("Domain world backfill: {Updated} filled, {Unresolved} still without a world ({Regions} regions reported)",
                updated, unresolved.Count, reported.Count);
            return new DomainWorldBackfillResultDto { Updated = updated, Unresolved = unresolved };
        }

        /// <summary>The domain's world when its Location, its region report and its parent don't contradict each other.</summary>
        private static string? Decide(Domain domain, Dictionary<int, Domain> byId, Dictionary<string, List<string>> reported,
            Dictionary<int, string?> locationWorlds)
        {
            var candidates = Candidates(domain, reported);
            string? parentWorld = ParentWorld(domain, byId);
            string? locationWorld = domain.LocationId is int locationId && locationWorlds.TryGetValue(locationId, out var lw)
                ? Normalize(lw)
                : null;

            bool FitsParent(string world) => parentWorld == null || Same(world, parentWorld);
            bool FitsRegion(string world) => candidates.Count == 0 || candidates.Any(c => Same(c, world));

            if (locationWorld != null)
            {
                return FitsParent(locationWorld) && FitsRegion(locationWorld) ? locationWorld : null;
            }
            if (candidates.Count == 1)
            {
                return FitsParent(candidates[0]) ? candidates[0] : null;
            }
            if (parentWorld != null && FitsRegion(parentWorld))
            {
                // Region in several worlds (or not found in any): the parent decides.
                return parentWorld;
            }
            return null;
        }

        private static List<string> Candidates(Domain domain, Dictionary<string, List<string>> reported) =>
            !string.IsNullOrWhiteSpace(domain.WgRegionId) && reported.TryGetValue(domain.WgRegionId.Trim(), out var worlds)
                ? worlds
                : new List<string>();

        private static string? ParentWorld(Domain domain, Dictionary<int, Domain> byId)
        {
            int parentId = domain switch
            {
                District district => district.TownId,
                Structure structure => structure.DistrictId,
                _ => 0
            };
            return parentId > 0 && byId.TryGetValue(parentId, out var parent) ? Normalize(parent.WorldName) : null;
        }

        private static DomainWorldMissingDto Missing(Domain domain, List<string> candidates) => new()
        {
            Id = domain.Id,
            Name = domain.Name,
            DomainType = domain.GetType().Name,
            WgRegionId = domain.WgRegionId,
            CandidateWorlds = candidates
        };

        private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string? Normalize(string? value)
        {
            string? trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}
