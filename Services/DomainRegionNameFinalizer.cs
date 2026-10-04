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
        /// Renames every domain region that still has a temporary name (domains created before rename-on-submit covered
        /// their type, or whose rename failed). Parents go first, so a child's region is set up under its parent's final name.
        /// </summary>
        Task<TempRegionFinalizeResult> FinalizeAllAsync();
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

        /// <summary>The concrete type name the plugin's managed-region policy understands; null for a bare Domain.</summary>
        public static string? DomainTypeOf(Domain domain) => domain switch
        {
            GateStructure => nameof(GateStructure),
            Structure => nameof(Structure),
            District => nameof(District),
            Town => nameof(Town),
            _ => null
        };

        public async Task<bool> FinalizeAsync(Domain domain)
        {
            if (domain == null || domain.Id <= 0 || !IsTemporary(domain.WgRegionId))
            {
                return false;
            }

            string tempName = domain.WgRegionId;
            string finalName = FinalNameFor(domain.Id);
            try
            {
                string? domainType = DomainTypeOf(domain);
                string? parentRegionId = await ParentRegionIdAsync(domain);
                _logger.LogInformation("Finalizing region name for {DomainType} {DomainId}: {TempName} -> {FinalName} (parent {ParentRegionId})",
                    domainType ?? nameof(Domain), domain.Id, tempName, finalName, parentRegionId);

                if (!await _regionService.RenameRegionAsync(tempName, finalName, domainType, parentRegionId))
                {
                    _logger.LogWarning("Failed to finalize region name for Domain {DomainId}: the plugin did not rename {TempName}",
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
                _logger.LogInformation("Finalized region name for Domain {DomainId}: {FinalName}", domain.Id, finalName);
                return true;
            }
            catch (Exception ex)
            {
                // The region is already renamed in WorldGuard: the domain now points at a region that no longer exists.
                _logger.LogError(ex,
                    "Region {TempName} was renamed to {FinalName} but Domain {DomainId} could not be updated; set its WgRegionId to {FinalName} by hand",
                    tempName, finalName, domain.Id, finalName);
                return false;
            }
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

            _logger.LogInformation("Temp region finalize pass: found {Found}, renamed {Renamed}, failed {Failed}",
                domains.Count, renamed, failed.Count);
            return new TempRegionFinalizeResult(domains.Count, renamed, failed);
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
