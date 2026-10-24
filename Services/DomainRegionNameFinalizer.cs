using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Gives a domain's WorldGuard region its final name once the domain exists. A region drawn through a WgRegionId world
    /// task is called <c>tempregion_worldtask_&lt;taskId&gt;</c>; after the Town, District, Structure, GateStructure or
    /// Domain is saved, the region is renamed to <c>domain_&lt;id&gt;</c> (all share Domain's id sequence) and the plugin
    /// sets it up as that type: parent region, priority and flags. See knk-workspace
    /// <c>docs/architecture/managed-worldguard-regions.md</c> §7 (KNG-43).
    /// </summary>
    public interface IDomainRegionNameFinalizer
    {
        /// <summary>
        /// Renames the domain's region when it still has a temporary name and stores the new name on the domain.
        /// Returns true when renamed. Never throws: the domain stays valid even when the plugin can't be reached, and the
        /// region keeps working under its temporary name until <see cref="FinalizeAllAsync"/> or the next save.
        /// </summary>
        Task<bool> FinalizeAsync(Domain domain);

        /// <summary>
        /// KNG-103: as <see cref="FinalizeAsync(Domain)"/> after the domain's region step was run again.
        /// <paramref name="previousRegionId"/> is the domain's WgRegionId before this save; the plugin replaces that region
        /// with the new one when it is safe to (see <see cref="DomainRegionNameFinalizer.ReplaceableRegionAsync"/>), so the
        /// new region takes the final name and the old one doesn't stay behind as an orphan.
        /// </summary>
        Task<bool> FinalizeAsync(Domain domain, string? previousRegionId);

        /// <summary>
        /// Renames every domain region that still has a temporary name (domains created before rename-on-submit covered
        /// their type, or whose rename failed). Parents go first, so a child's region is set up under its parent's final name.
        /// </summary>
        Task<TempRegionFinalizeResult> FinalizeAllAsync();

        /// <summary>
        /// KNG-254: gives a domain sub-region its final name <c>domain_&lt;domainId&gt;_sub_&lt;id&gt;</c> after it is saved,
        /// as <see cref="FinalizeAsync(Domain, string?)"/> does for the main region: a temp-named region drawn through its
        /// world task, or an existing region adopted with "select", is renamed and set up by the plugin as a copy of the
        /// domain's main region. <paramref name="previousRegionId"/> is the sub-region's WgRegionId before this save, for
        /// a re-run of its world task (replaced like KNG-103). Never throws.
        /// </summary>
        Task<bool> FinalizeSubRegionAsync(DomainSubRegion subRegion, string? previousRegionId);
    }

    public sealed record TempRegionFinalizeResult(int Found, int Renamed, IReadOnlyList<string> Failed);

    public class DomainRegionNameFinalizer : IDomainRegionNameFinalizer
    {
        public const string TempRegionPrefix = "tempregion_worldtask_";

        private readonly KnKDbContext _context;
        private readonly IRegionService _regionService;
        private readonly ILogger<DomainRegionNameFinalizer> _logger;

        public DomainRegionNameFinalizer(KnKDbContext context, IRegionService regionService, ILogger<DomainRegionNameFinalizer> logger)
        {
            _context = context;
            _regionService = regionService;
            _logger = logger;
        }

        public static bool IsTemporary(string? regionId) =>
            !string.IsNullOrWhiteSpace(regionId) && regionId.StartsWith(TempRegionPrefix, StringComparison.OrdinalIgnoreCase);

        public static string FinalNameFor(int domainId) => $"domain_{domainId}";

        /// <summary>KNG-254: the final name of a domain's sub-region.</summary>
        public static string FinalSubRegionNameFor(int domainId, int subRegionId) => $"domain_{domainId}_sub_{subRegionId}";

        /// <summary>The concrete type name the plugin's managed-region policy understands; null for a bare Domain.</summary>
        public static string? DomainTypeOf(Domain domain) => domain switch
        {
            GateStructure => nameof(GateStructure),
            Structure => nameof(Structure),
            District => nameof(District),
            Town => nameof(Town),
            _ => null
        };

        public Task<bool> FinalizeAsync(Domain domain) => FinalizeAsync(domain, null);

        public async Task<bool> FinalizeAsync(Domain domain, string? previousRegionId)
        {
            if (domain == null || domain.Id <= 0 || !IsTemporary(domain.WgRegionId))
            {
                return false;
            }

            string tempName = domain.WgRegionId;
            string finalName = FinalNameFor(domain.Id);
            string? replaceRegionId = null;
            try
            {
                string? domainType = DomainTypeOf(domain);
                string? parentRegionId = await ParentRegionIdAsync(domain);
                replaceRegionId = await ReplaceableRegionAsync(domain, previousRegionId);
                _logger.LogInformation("Finalizing region name for {DomainType} {DomainId}: {TempName} -> {FinalName} (parent {ParentRegionId}, world {World}, replacing {ReplaceRegionId})",
                    domainType ?? nameof(Domain), domain.Id, tempName, finalName, parentRegionId, domain.WorldName, replaceRegionId ?? "nothing");

                bool renamed = replaceRegionId == null
                    ? await _regionService.RenameRegionAsync(tempName, finalName, domainType, parentRegionId, domain.WorldName)
                    : await _regionService.RenameRegionAsync(tempName, finalName, domainType, parentRegionId, domain.WorldName, replaceRegionId);
                if (!renamed)
                {
                    _logger.LogWarning("Failed to finalize region name for Domain {DomainId}: the plugin did not rename {TempName} (see the plugin log)",
                        domain.Id, tempName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finalizing region name for Domain {DomainId} ({TempName})", domain.Id, tempName);
                return false;
            }

            try
            {
                await StoreRegionIdAsync(domain, finalName);
                _logger.LogInformation("Finalized region name for Domain {DomainId}: {FinalName}{Replaced}", domain.Id, finalName,
                    replaceRegionId != null ? $" (replaced {replaceRegionId})" : string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                // The region is renamed in WorldGuard but the domain still says tempName. Rename it back so the stored id
                // names a region that exists; the next save or finalize-temp-names finishes the job.
                domain.WgRegionId = tempName;
                await RenameBackAsync(domain, tempName, finalName, ex);
                return false;
            }
        }

        /// <summary>
        /// KNG-103: which region, if any, the plugin may replace with the domain's new one. The API decides from the
        /// database; the plugin double-checks the name. Only the domain's own regions qualify: its final name
        /// (<c>domain_&lt;id&gt;</c>, redefined in place) or a temp-named region (removed), and only when no other domain
        /// in the same world uses it. Anything else, such as a v1 region chosen with "select", is left alone.
        /// </summary>
        public async Task<string?> ReplaceableRegionAsync(Domain domain, string? previousRegionId)
        {
            if (string.IsNullOrWhiteSpace(previousRegionId))
            {
                return null;
            }
            string previous = previousRegionId.Trim();
            if (previous.Equals(domain.WgRegionId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (!previous.Equals(FinalNameFor(domain.Id), StringComparison.OrdinalIgnoreCase) && !IsTemporary(previous))
            {
                _logger.LogInformation("Domain {DomainId}'s previous region {PreviousRegionId} is not one the API creates; it is kept",
                    domain.Id, previous);
                return null;
            }

            string? world = string.IsNullOrWhiteSpace(domain.WorldName) ? null : domain.WorldName;
            var sharedWith = await _context.Domains
                .AsNoTracking()
                .Where(d => d.Id != domain.Id && d.WgRegionId == previous)
                .Select(d => new { d.Id, d.WorldName })
                .ToListAsync();
            // KNG-111: ids are unique per world; a domain without a stored world might be in any of them.
            var other = sharedWith.FirstOrDefault(d => world == null || string.IsNullOrWhiteSpace(d.WorldName)
                || d.WorldName.Equals(world, StringComparison.OrdinalIgnoreCase));
            if (other != null)
            {
                _logger.LogWarning("Domain {DomainId}'s previous region {PreviousRegionId} is also Domain {OtherDomainId}'s; it is kept",
                    domain.Id, previous, other.Id);
                return null;
            }
            return previous;
        }

        private async Task RenameBackAsync(Domain domain, string tempName, string finalName, Exception storeError)
        {
            try
            {
                if (await _regionService.RenameRegionAsync(finalName, tempName, null, null, domain.WorldName))
                {
                    _logger.LogError(storeError,
                        "Domain {DomainId} could not be updated after its region was renamed to {FinalName}; the region was renamed back to {TempName}, which the domain still names",
                        domain.Id, finalName, tempName);
                    return;
                }
            }
            catch (Exception renameBackError)
            {
                _logger.LogError(renameBackError, "Renaming {FinalName} back to {TempName} failed", finalName, tempName);
            }
            _logger.LogError(storeError,
                "Region {TempName} was renamed to {FinalName} but Domain {DomainId} could not be updated and the rename could not be undone; set its WgRegionId to {FinalName} by hand",
                tempName, finalName, domain.Id, finalName);
        }

        public async Task<TempRegionFinalizeResult> FinalizeAllAsync()
        {
            var domains = await _context.Domains
                .Where(d => d.WgRegionId.StartsWith(TempRegionPrefix))
                .ToListAsync();

            int renamed = 0;
            var failed = new List<string>();
            foreach (var domain in domains.OrderBy(HierarchyLevel).ThenBy(d => d.Id))
            {
                string tempName = domain.WgRegionId;
                if (await FinalizeAsync(domain))
                {
                    renamed++;
                }
                else
                {
                    failed.Add($"{DomainTypeOf(domain) ?? nameof(Domain)} {domain.Id} ({tempName})");
                }
            }

            // KNG-254: then the sub-regions not yet on their final name (temp-named, or adopted and not renamed yet), so
            // each is set up under its domain's final main region.
            var subRegions = (await _context.DomainSubRegions.ToListAsync())
                .Where(s => !s.WgRegionId.Equals(FinalSubRegionNameFor(s.DomainId, s.Id), StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Id)
                .ToList();
            foreach (var subRegion in subRegions)
            {
                string currentName = subRegion.WgRegionId;
                if (await FinalizeSubRegionAsync(subRegion, null))
                {
                    renamed++;
                }
                else
                {
                    failed.Add($"Sub-region {subRegion.Id} of Domain {subRegion.DomainId} ({currentName})");
                }
            }

            int found = domains.Count + subRegions.Count;
            _logger.LogInformation("Temp region finalize pass: found {Found}, renamed {Renamed}, failed {Failed}",
                found, renamed, failed.Count);
            return new TempRegionFinalizeResult(found, renamed, failed);
        }

        public async Task<bool> FinalizeSubRegionAsync(DomainSubRegion subRegion, string? previousRegionId)
        {
            if (subRegion == null || subRegion.Id <= 0 || subRegion.DomainId <= 0 || string.IsNullOrWhiteSpace(subRegion.WgRegionId))
            {
                return false;
            }
            string finalName = FinalSubRegionNameFor(subRegion.DomainId, subRegion.Id);
            string currentName = subRegion.WgRegionId;
            if (currentName.Equals(finalName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var domain = await _context.Domains.AsNoTracking().FirstOrDefaultAsync(d => d.Id == subRegion.DomainId);
            if (domain == null || string.IsNullOrWhiteSpace(domain.WgRegionId))
            {
                _logger.LogWarning("Sub-region {SubRegionId} keeps {RegionId}: its domain {DomainId} has no main region to copy",
                    subRegion.Id, currentName, subRegion.DomainId);
                return false;
            }

            string? replaceRegionId = null;
            try
            {
                string? domainType = DomainTypeOf(domain);
                replaceRegionId = await ReplaceableSubRegionAsync(subRegion, previousRegionId, finalName, domain.WorldName);
                _logger.LogInformation("Finalizing sub-region {SubRegionId} of {DomainType} {DomainId}: {CurrentName} -> {FinalName} (copy of {MainRegion}, world {World}, replacing {ReplaceRegionId})",
                    subRegion.Id, domainType ?? nameof(Domain), domain.Id, currentName, finalName, domain.WgRegionId, domain.WorldName, replaceRegionId ?? "nothing");

                if (!await _regionService.RenameSubRegionAsync(currentName, finalName, domainType, domain.WgRegionId, domain.WorldName, replaceRegionId))
                {
                    _logger.LogWarning("Failed to finalize sub-region {SubRegionId}: the plugin did not rename {CurrentName} (see the plugin log)",
                        subRegion.Id, currentName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finalizing sub-region {SubRegionId} ({CurrentName})", subRegion.Id, currentName);
                return false;
            }

            try
            {
                subRegion.WgRegionId = finalName;
                if (_context.Entry(subRegion).State == EntityState.Detached)
                {
                    var tracked = await _context.DomainSubRegions.FirstOrDefaultAsync(s => s.Id == subRegion.Id)
                        ?? throw new InvalidOperationException($"Sub-region {subRegion.Id} not found.");
                    tracked.WgRegionId = finalName;
                }
                await _context.SaveChangesAsync();
                _logger.LogInformation("Finalized sub-region {SubRegionId} of Domain {DomainId}: {FinalName}{Replaced}", subRegion.Id, domain.Id,
                    finalName, replaceRegionId != null ? $" (replaced {replaceRegionId})" : string.Empty);
                return true;
            }
            catch (Exception storeError)
            {
                subRegion.WgRegionId = currentName;
                try
                {
                    if (await _regionService.RenameRegionAsync(finalName, currentName, null, null, domain.WorldName))
                    {
                        _logger.LogError(storeError, "Sub-region {SubRegionId} could not be updated after its region was renamed to {FinalName}; renamed back to {CurrentName}",
                            subRegion.Id, finalName, currentName);
                        return false;
                    }
                }
                catch (Exception renameBackError)
                {
                    _logger.LogError(renameBackError, "Renaming {FinalName} back to {CurrentName} failed", finalName, currentName);
                }
                _logger.LogError(storeError, "Region {CurrentName} was renamed to {FinalName} but sub-region {SubRegionId} could not be updated; set its WgRegionId to {FinalName} by hand",
                    currentName, finalName, subRegion.Id, finalName);
                return false;
            }
        }

        /// <summary>
        /// KNG-254: as <see cref="ReplaceableRegionAsync"/> for a sub-region whose world task was run again: only its own
        /// final name or a temp-named region qualify, and only when no other domain or sub-region in the world uses it.
        /// </summary>
        public async Task<string?> ReplaceableSubRegionAsync(DomainSubRegion subRegion, string? previousRegionId, string finalName, string? world)
        {
            if (string.IsNullOrWhiteSpace(previousRegionId))
            {
                return null;
            }
            string previous = previousRegionId.Trim();
            if (previous.Equals(subRegion.WgRegionId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (!previous.Equals(finalName, StringComparison.OrdinalIgnoreCase) && !IsTemporary(previous))
            {
                _logger.LogInformation("Sub-region {SubRegionId}'s previous region {PreviousRegionId} is not one the API creates; it is kept",
                    subRegion.Id, previous);
                return null;
            }
            string? takenBy = await DomainRegionOwnership.TakenByAsync(_context, previous, world, exceptSubRegionId: subRegion.Id);
            if (takenBy != null)
            {
                _logger.LogWarning("Sub-region {SubRegionId}'s previous region {PreviousRegionId} is kept: {TakenBy}", subRegion.Id, previous, takenBy);
                return null;
            }
            return previous;
        }

        private static int HierarchyLevel(Domain domain) => domain switch
        {
            Town => 0,
            District => 1,
            Structure => 2,
            _ => 3
        };

        private async Task<string?> ParentRegionIdAsync(Domain domain)
        {
            int parentId = domain switch
            {
                District district => district.TownId,
                Structure structure => structure.DistrictId,
                _ => 0
            };
            if (parentId <= 0)
            {
                return null;
            }

            return await _context.Domains
                .AsNoTracking()
                .Where(d => d.Id == parentId)
                .Select(d => d.WgRegionId)
                .FirstOrDefaultAsync();
        }

        private async Task StoreRegionIdAsync(Domain domain, string finalName)
        {
            domain.WgRegionId = finalName;
            if (_context.Entry(domain).State == EntityState.Detached)
            {
                // Saved through another context instance (or read without tracking): update the tracked row instead.
                var tracked = await _context.Domains.FirstOrDefaultAsync(d => d.Id == domain.Id)
                    ?? throw new InvalidOperationException($"Domain {domain.Id} not found.");
                tracked.WgRegionId = finalName;
            }
            await _context.SaveChangesAsync();
        }
    }
}
