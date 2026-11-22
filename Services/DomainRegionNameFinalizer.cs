using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
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
    /// <c>docs/architecture/managed-worldguard-regions.md</c> §7 (KNG-43). The same rename on save covers domain sub-regions
    /// (KNG-254), lootbox spawn areas (PandiO/knk-workspace#34), landmarks (PandiO/knk-workspace#945) and production areas
    /// (PandiO/knk-workspace#946), the last three through one path (<see cref="RegionOwnerKind"/>).
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

        /// <summary>
        /// PandiO/knk-workspace#34: gives a lootbox spawn area's region drawn through the form's region task its final name
        /// <c>lootbox_area_&lt;id&gt;</c> after the area is saved, so the 14-day temp-region cleanup keeps it. The region
        /// keeps the outline and Y range it was drawn with (PandiO/knk-workspace#487). Only a temp-named region is
        /// renamed; a borrowed region (a town) or a <c>lootbox_&lt;name&gt;</c> region from the in-game command is left as it
        /// is. <paramref name="previousRegionId"/> is the area's WgRegionId before this save: on a re-run of the region
        /// task its previous <c>lootbox_area_&lt;id&gt;</c> is redefined in place, a previous temp-named region removed
        /// (as KNG-103 does for domains). Never throws.
        /// </summary>
        Task<bool> FinalizeLootboxAreaAsync(LootboxSpawnArea area, string? previousRegionId);

        /// <summary>
        /// PandiO/knk-workspace#945: gives a landmark's region drawn through the form's region task its final name
        /// <c>landmark_&lt;id&gt;</c> after the landmark is saved, so the 14-day temp-region cleanup keeps it, exactly as
        /// <see cref="FinalizeLootboxAreaAsync"/> does for a lootbox spawn area. The region keeps the outline and Y range it
        /// was drawn with and gets no managed-region setup. Only a temp-named region is renamed; an existing region the
        /// landmark names (a domain's, a hand-made one, one given to <c>/knk landmark create &lt;name&gt; region &lt;id&gt;</c>)
        /// is left as it is. <paramref name="previousRegionId"/> is the landmark's RegionId before this save: on a re-run of
        /// the region task its previous <c>landmark_&lt;id&gt;</c> is redefined in place, a previous temp-named region removed
        /// (KNG-103). The world is the one of the landmark's pin. Never throws.
        /// </summary>
        Task<bool> FinalizeLandmarkAsync(Landmark landmark, string? previousRegionId);

        /// <summary>
        /// PandiO/knk-workspace#946: the same for a Region-shaped production area: a region drawn through the area form's
        /// region task becomes <c>production_area_&lt;id&gt;</c> in the area's world (no managed-region setup, the drawn
        /// outline and Y range kept), a re-run replaces it, and an existing region the area names is kept. Never throws.
        /// </summary>
        Task<bool> FinalizeProductionAreaAsync(ProductionArea area, string? previousRegionId);
    }

    /// <summary>
    /// #946: the rows besides domains and domain sub-regions that stand on a region drawn through their form's region task
    /// and get a final name for it (#34, #945, #946).
    /// </summary>
    public enum RegionOwnerKind
    {
        LootboxSpawnArea,
        Landmark,
        ProductionArea,
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

        /// <summary>
        /// KNG-269: the domain's region is a new shape now, so every stored validation result that was
        /// about this domain, or read it as a dependency (a Location checked against its region), is
        /// stale. The rows are dropped; the next form run checks again. Never fails the finalize.
        /// </summary>
        private async Task ForgetValidationResultsAsync(Domain domain)
        {
            try
            {
                // #991: every type name along the chain, not only the one sent to the plugin (a Shop form's results are
                // stored as "Shop", which DomainTypeOf used to send as "Structure").
                var types = TypeNamesOf(domain);
                var keys = types.Select(t => $"{t}:{domain.Id}").ToList();
                var rows = await _context.FieldValidationRunLogs
                    .Where(r => (types.Contains(r.SubjectType) && r.SubjectId == domain.Id) || (r.DependencyKey != null && keys.Contains(r.DependencyKey)))
                    .ToListAsync();
                if (rows.Count == 0) return;
                _context.FieldValidationRunLogs.RemoveRange(rows);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Dropped {Count} stored validation results about Domain {DomainId} after its region changed", rows.Count, domain.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not drop the stored validation results about Domain {DomainId}", domain.Id);
            }
        }

        public static bool IsTemporary(string? regionId) =>
            !string.IsNullOrWhiteSpace(regionId) && regionId.StartsWith(TempRegionPrefix, StringComparison.OrdinalIgnoreCase);

        public static string FinalNameFor(int domainId) => $"domain_{domainId}";

        /// <summary>KNG-254: the final name of a domain's sub-region.</summary>
        public static string FinalSubRegionNameFor(int domainId, int subRegionId) => $"domain_{domainId}_sub_{subRegionId}";

        /// <summary>
        /// #34: the final name of a lootbox spawn area's drawn region. It starts with the in-game command's
        /// <c>lootbox_</c>, so it belongs to that one area (<c>LootboxSpawnAreaService</c>'s RegionInUse rule) and an
        /// in-game delete of the area removes it; area names <c>area_&lt;n&gt;</c> are reserved so the command's
        /// <c>lootbox_&lt;name&gt;</c> can't take it.
        /// </summary>
        public static string FinalLootboxAreaNameFor(int areaId) => $"lootbox_area_{areaId}";

        /// <summary>
        /// #945: the final name of a landmark's drawn region. No landmark name is reserved for it: a landmark's name never
        /// becomes a region id (the in-game command only takes an existing region), and the plugin refuses to rename onto
        /// a region that already holds the name unless it is the one being replaced.
        /// </summary>
        public static string FinalLandmarkNameFor(int landmarkId) => $"landmark_{landmarkId}";

        /// <summary>
        /// #946: the final name of a production area's drawn region. No area name is reserved: an area's name never becomes
        /// a region id, and the plugin refuses to rename onto a region that already holds the name.
        /// </summary>
        public static string FinalProductionAreaNameFor(int areaId) => $"production_area_{areaId}";

        /// <summary>The concrete type name the plugin's managed-region policy understands; null for a bare Domain.</summary>
        public static string? DomainTypeOf(Domain domain) => domain switch
        {
            GateStructure => nameof(GateStructure),
            // PandiO/knk-workspace#991: the subtypes the startup repair reads under their own name (DomainMappingProfile's
            // GetType().Name), so a new region is set up as the repair would set it up: ProductionStructure as
            // RESOURCE_PRODUCTION and Warehouse as STRUCTURE with the plugin's production.enabled, Shop as STRUCTURE, and
            // CrownLand as CROWN_LAND (only with production.enabled, as the repair). Shop before ProductionStructure, its
            // base type.
            Shop => nameof(Shop),
            ProductionStructure => nameof(ProductionStructure),
            Warehouse => nameof(Warehouse),
            Structure => nameof(Structure),
            District => nameof(District),
            Town => nameof(Town),
            CrownLand => nameof(CrownLand),
            _ => null
        };

        /// <summary>
        /// PandiO/knk-workspace#991: the domain's type name and its base types' names down to <c>Domain</c> (a shop:
        /// Shop, ProductionStructure, Structure, Domain). Stored validation results carry the form's entity name as their
        /// subject type and the related field's object type in their dependency key, which can be any of these.
        /// </summary>
        public static List<string> TypeNamesOf(Domain domain)
        {
            var names = new List<string>();
            for (var type = domain.GetType(); type != null && typeof(Domain).IsAssignableFrom(type); type = type.BaseType)
            {
                names.Add(type.Name);
            }
            return names;
        }

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
                await ForgetValidationResultsAsync(domain);
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
            // #34, #945, #946: a temp-named previous region is removed; a lootbox spawn area, landmark or production area
            // standing on it would lose its region.
            if (IsTemporary(previous) && await OwnedRegionUserAsync(previous, world, null) is string user)
            {
                _logger.LogWarning("Domain {DomainId}'s previous region {PreviousRegionId} is also {User}'s; it is kept",
                    domain.Id, previous, user);
                return null;
            }
            return previous;
        }

        /// <summary>
        /// #34: the id of a lootbox spawn area (other than <paramref name="exceptAreaId"/>) standing on
        /// <paramref name="regionId"/> in <paramref name="world"/>, or null. Without a world, an area in any world counts.
        /// </summary>
        private async Task<int?> LootboxAreaUsingAsync(string regionId, string? world, int? exceptAreaId)
        {
            string loweredRegion = regionId.Trim().ToLower();
            string? loweredWorld = string.IsNullOrWhiteSpace(world) ? null : world.Trim().ToLower();
            int self = exceptAreaId ?? 0;
            var ids = await _context.LootboxSpawnAreas.AsNoTracking()
                .Where(a => a.Id != self && a.WgRegionId.ToLower() == loweredRegion
                    && (loweredWorld == null || a.World.ToLower() == loweredWorld))
                .OrderBy(a => a.Id)
                .Select(a => a.Id)
                .Take(1)
                .ToListAsync();
            return ids.Count == 0 ? null : ids[0];
        }

        /// <summary>
        /// #945: the id of a landmark (other than <paramref name="exceptLandmarkId"/>) whose extent is
        /// <paramref name="regionId"/> with its pin in <paramref name="world"/>, or null. Without a world, a landmark in any
        /// world counts.
        /// </summary>
        private async Task<int?> LandmarkUsingAsync(string regionId, string? world, int? exceptLandmarkId)
        {
            string loweredRegion = regionId.Trim().ToLower();
            string? loweredWorld = string.IsNullOrWhiteSpace(world) ? null : world.Trim().ToLower();
            int self = exceptLandmarkId ?? 0;
            var ids = await _context.Landmarks.AsNoTracking()
                .Where(l => l.Id != self && l.RegionId != null && l.RegionId.ToLower() == loweredRegion
                    && (loweredWorld == null || (l.Location.World != null && l.Location.World.ToLower() == loweredWorld)))
                .OrderBy(l => l.Id)
                .Select(l => l.Id)
                .Take(1)
                .ToListAsync();
            return ids.Count == 0 ? null : ids[0];
        }

        /// <summary>
        /// #946: the id of a production area (other than <paramref name="exceptAreaId"/>) whose region is
        /// <paramref name="regionId"/> in <paramref name="world"/>, or null. Without a world, an area in any world counts.
        /// </summary>
        private async Task<int?> ProductionAreaUsingAsync(string regionId, string? world, int? exceptAreaId)
        {
            string loweredRegion = regionId.Trim().ToLower();
            string? loweredWorld = string.IsNullOrWhiteSpace(world) ? null : world.Trim().ToLower();
            int self = exceptAreaId ?? 0;
            var ids = await _context.ProductionAreas.AsNoTracking()
                .Where(a => a.Id != self && a.WgRegionId != null && a.WgRegionId.ToLower() == loweredRegion
                    && (loweredWorld == null || a.WorldName.ToLower() == loweredWorld))
                .OrderBy(a => a.Id)
                .Select(a => a.Id)
                .Take(1)
                .ToListAsync();
            return ids.Count == 0 ? null : ids[0];
        }

        /// <summary>#946: one row of <see cref="RegionOwnerKind"/>, to leave it out when asking who else uses a region.</summary>
        private readonly record struct RegionOwnerRef(RegionOwnerKind Kind, int Id);

        /// <summary>
        /// #34, #945, #946: the lootbox spawn area, landmark or production area (other than <paramref name="self"/>) that
        /// stands on <paramref name="regionId"/> in <paramref name="world"/>, as "landmark 5", or null.
        /// </summary>
        private async Task<string?> OwnedRegionUserAsync(string regionId, string? world, RegionOwnerRef? self)
        {
            int Except(RegionOwnerKind kind) => self is { } s && s.Kind == kind ? s.Id : 0;
            if (await LootboxAreaUsingAsync(regionId, world, Except(RegionOwnerKind.LootboxSpawnArea)) is int areaId)
            {
                return $"lootbox spawn area {areaId}";
            }
            if (await LandmarkUsingAsync(regionId, world, Except(RegionOwnerKind.Landmark)) is int landmarkId)
            {
                return $"landmark {landmarkId}";
            }
            if (await ProductionAreaUsingAsync(regionId, world, Except(RegionOwnerKind.ProductionArea)) is int productionAreaId)
            {
                return $"production area {productionAreaId}";
            }
            return null;
        }

        /// <summary>
        /// What else (a domain, a domain sub-region, or a lootbox spawn area, landmark or production area other than
        /// <paramref name="self"/>) stands on <paramref name="regionId"/> in <paramref name="world"/>, or null.
        /// </summary>
        private async Task<string?> OtherUserOfRegionAsync(string regionId, string? world, RegionOwnerRef? self)
        {
            string? takenBy = await DomainRegionOwnership.TakenByAsync(_context, regionId, world);
            if (takenBy == null && await OwnedRegionUserAsync(regionId, world, self) is string user)
            {
                takenBy = $"{user} stands on it";
            }
            return takenBy;
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

            // #34, #945, #946: then the lootbox spawn areas, landmarks and production areas still on a region drawn through
            // their form (rename failed or the server was down at the save).
            var areas = await _context.LootboxSpawnAreas
                .Where(a => a.WgRegionId.StartsWith(TempRegionPrefix))
                .OrderBy(a => a.Id)
                .ToListAsync();
            renamed += await FinalizeEachAsync(areas, a => FinalizeLootboxAreaAsync(a, null),
                a => $"{nameof(LootboxSpawnArea)} {a.Id} ({a.WgRegionId})", failed);
            var landmarks = await _context.Landmarks
                .Where(l => l.RegionId != null && l.RegionId.StartsWith(TempRegionPrefix))
                .OrderBy(l => l.Id)
                .ToListAsync();
            renamed += await FinalizeEachAsync(landmarks, l => FinalizeLandmarkAsync(l, null),
                l => $"{nameof(Landmark)} {l.Id} ({l.RegionId})", failed);
            var productionAreas = await _context.ProductionAreas
                .Where(a => a.Shape == ProductionAreaShape.Region && a.WgRegionId != null && a.WgRegionId.StartsWith(TempRegionPrefix))
                .OrderBy(a => a.Id)
                .ToListAsync();
            renamed += await FinalizeEachAsync(productionAreas, a => FinalizeProductionAreaAsync(a, null),
                a => $"{nameof(ProductionArea)} {a.Id} ({a.WgRegionId})", failed);

            int found = domains.Count + subRegions.Count + areas.Count + landmarks.Count + productionAreas.Count;
            _logger.LogInformation("Temp region finalize pass: found {Found}, renamed {Renamed}, failed {Failed}",
                found, renamed, failed.Count);
            return new TempRegionFinalizeResult(found, renamed, failed);
        }

        /// <summary>
        /// #946: finalizes each row, counting the renames; a row that is not renamed is added to <paramref name="failed"/> as
        /// <paramref name="describe"/> says before the attempt (its temp name).
        /// </summary>
        private static async Task<int> FinalizeEachAsync<T>(List<T> rows, Func<T, Task<bool>> finalize, Func<T, string> describe,
            List<string> failed)
        {
            int renamed = 0;
            foreach (var row in rows)
            {
                string description = describe(row);
                if (await finalize(row))
                {
                    renamed++;
                }
                else
                {
                    failed.Add(description);
                }
            }
            return renamed;
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
                await ForgetValidationResultsAsync(domain);
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
            if (takenBy == null && IsTemporary(previous) && await OwnedRegionUserAsync(previous, world, null) is string user)
            {
                takenBy = $"{user} stands on it";
            }
            if (takenBy != null)
            {
                _logger.LogWarning("Sub-region {SubRegionId}'s previous region {PreviousRegionId} is kept: {TakenBy}", subRegion.Id, previous, takenBy);
                return null;
            }
            return previous;
        }

        /// <summary>
        /// #946: a row of <see cref="RegionOwnerKind"/> whose drawn region gets its final name: who it is, its current
        /// region and world, the final name, the plugin call that renames it, and how its region id is stored and reset.
        /// </summary>
        private sealed record OwnedRegion(RegionOwnerRef Self, string Label, string? RegionId, string FinalName, string? World,
            Func<string, string, string?, string?, Task<bool>> Rename, Func<string, Task> StoreAsync, Action<string> SetLocal);

        private static string? WorldOrNull(string? world) => string.IsNullOrWhiteSpace(world) ? null : world.Trim();

        private OwnedRegion LootboxAreaOwner(LootboxSpawnArea area, string finalName, string? world) => new(
            new RegionOwnerRef(RegionOwnerKind.LootboxSpawnArea, area.Id), $"lootbox spawn area {area.Id}", area.WgRegionId,
            finalName, world, _regionService.RenameLootboxAreaRegionAsync,
            name => StoreOwnedRegionIdAsync(area, (a, n) => a.WgRegionId = n,
                () => _context.LootboxSpawnAreas.FirstOrDefaultAsync(a => a.Id == area.Id), name),
            name => area.WgRegionId = name);

        private OwnedRegion LandmarkOwner(Landmark landmark, string finalName, string? world) => new(
            new RegionOwnerRef(RegionOwnerKind.Landmark, landmark.Id), $"landmark {landmark.Id}", landmark.RegionId,
            finalName, world, _regionService.RenameLandmarkRegionAsync,
            name => StoreOwnedRegionIdAsync(landmark, (l, n) => l.RegionId = n,
                () => _context.Landmarks.FirstOrDefaultAsync(l => l.Id == landmark.Id), name),
            name => landmark.RegionId = name);

        private OwnedRegion ProductionAreaOwner(ProductionArea area, string finalName, string? world) => new(
            new RegionOwnerRef(RegionOwnerKind.ProductionArea, area.Id), $"production area {area.Id}", area.WgRegionId,
            finalName, world, _regionService.RenameProductionAreaRegionAsync,
            name => StoreOwnedRegionIdAsync(area, (a, n) => a.WgRegionId = n,
                () => _context.ProductionAreas.FirstOrDefaultAsync(a => a.Id == area.Id), name),
            name => area.WgRegionId = name);

        public async Task<bool> FinalizeLootboxAreaAsync(LootboxSpawnArea area, string? previousRegionId)
        {
            if (area == null)
            {
                return false;
            }
            return await FinalizeOwnedRegionAsync(LootboxAreaOwner(area, FinalLootboxAreaNameFor(area.Id), WorldOrNull(area.World)),
                previousRegionId);
        }

        public async Task<bool> FinalizeLandmarkAsync(Landmark landmark, string? previousRegionId)
        {
            if (landmark == null || landmark.Id <= 0 || !IsTemporary(landmark.RegionId))
            {
                return false;
            }
            string? world;
            try
            {
                world = await LandmarkWorldAsync(landmark);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finalizing region name for landmark {LandmarkId} ({TempName}): its world could not be read",
                    landmark.Id, landmark.RegionId);
                return false;
            }
            return await FinalizeOwnedRegionAsync(LandmarkOwner(landmark, FinalLandmarkNameFor(landmark.Id), world), previousRegionId);
        }

        public async Task<bool> FinalizeProductionAreaAsync(ProductionArea area, string? previousRegionId)
        {
            if (area == null || area.Shape != ProductionAreaShape.Region)
            {
                return false;
            }
            return await FinalizeOwnedRegionAsync(
                ProductionAreaOwner(area, FinalProductionAreaNameFor(area.Id), WorldOrNull(area.WorldName)), previousRegionId);
        }

        /// <summary>
        /// #34, #945, #946: the rename on save of a lootbox spawn area's, landmark's or production area's drawn region.
        /// Only a temp-named region is renamed; one someone else (a domain, a sub-region, another such row) also stands on
        /// is kept on its temp name, which the cleanup keeps. A failed rename never throws; storing after a successful
        /// rename failing renames the region back.
        /// </summary>
        private async Task<bool> FinalizeOwnedRegionAsync(OwnedRegion owner, string? previousRegionId)
        {
            if (owner.Self.Id <= 0 || !IsTemporary(owner.RegionId))
            {
                return false;
            }
            string tempName = owner.RegionId!.Trim();
            string finalName = owner.FinalName;
            string? world = owner.World;
            string? replaceRegionId = null;
            try
            {
                // The temp region is renamed away; whoever else stands on it would lose it. Left on its temp name, the
                // cleanup still keeps it (the plugin asks for these rows too).
                string? sharedWith = await OtherUserOfRegionAsync(tempName, world, owner.Self);
                if (sharedWith != null)
                {
                    _logger.LogWarning("{Owner} keeps {TempName}: {SharedWith}", owner.Label, tempName, sharedWith);
                    return false;
                }

                replaceRegionId = await ReplaceableOwnedRegionAsync(owner, previousRegionId);
                _logger.LogInformation("Finalizing region name for {Owner}: {TempName} -> {FinalName} (world {World}, replacing {ReplaceRegionId})",
                    owner.Label, tempName, finalName, world, replaceRegionId ?? "nothing");
                if (!await owner.Rename(tempName, finalName, world, replaceRegionId))
                {
                    _logger.LogWarning("Failed to finalize region name for {Owner}: the plugin did not rename {TempName} (see the plugin log)",
                        owner.Label, tempName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finalizing region name for {Owner} ({TempName})", owner.Label, tempName);
                return false;
            }

            try
            {
                await owner.StoreAsync(finalName);
                _logger.LogInformation("Finalized region name for {Owner}: {FinalName}{Replaced}", owner.Label, finalName,
                    replaceRegionId != null ? $" (replaced {replaceRegionId})" : string.Empty);
                return true;
            }
            catch (Exception storeError)
            {
                owner.SetLocal(tempName);
                try
                {
                    if (await _regionService.RenameRegionAsync(finalName, tempName, null, null, world))
                    {
                        _logger.LogError(storeError, "{Owner} could not be updated after its region was renamed to {FinalName}; renamed back to {TempName}",
                            owner.Label, finalName, tempName);
                        return false;
                    }
                }
                catch (Exception renameBackError)
                {
                    _logger.LogError(renameBackError, "Renaming {FinalName} back to {TempName} failed", finalName, tempName);
                }
                _logger.LogError(storeError, "Region {TempName} was renamed to {FinalName} but {Owner} could not be updated; set its region id to {FinalName} by hand",
                    tempName, finalName, owner.Label, finalName);
                return false;
            }
        }

        /// <summary>Sets the region id on <paramref name="entity"/> (or its tracked row when it is detached) and saves.</summary>
        private async Task StoreOwnedRegionIdAsync<T>(T entity, Action<T, string> setRegion, Func<Task<T?>> loadTracked, string regionId)
            where T : class
        {
            setRegion(entity, regionId);
            if (_context.Entry(entity).State == EntityState.Detached)
            {
                var tracked = await loadTracked() ?? throw new InvalidOperationException($"{typeof(T).Name} not found.");
                setRegion(tracked, regionId);
            }
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// #34, #945, #946: as <see cref="ReplaceableRegionAsync"/> for a row whose region task was run again. Only its own
        /// final name (redefined in place) or a temp-named region (removed) qualify. A temp-named one is kept when a domain,
        /// domain sub-region or other lootbox spawn area, landmark or production area in the world uses it; the own final
        /// name when a domain, sub-region or other lootbox area holds it (<see cref="RegionHolderAsync"/>). Anything else
        /// it named before (a borrowed region, a hand-made one, an in-game <c>lootbox_&lt;name&gt;</c>) is kept.
        /// </summary>
        private async Task<string?> ReplaceableOwnedRegionAsync(OwnedRegion owner, string? previousRegionId)
        {
            if (string.IsNullOrWhiteSpace(previousRegionId))
            {
                return null;
            }
            string previous = previousRegionId.Trim();
            if (previous.Equals(owner.RegionId?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (!previous.Equals(owner.FinalName, StringComparison.OrdinalIgnoreCase) && !IsTemporary(previous))
            {
                _logger.LogInformation("{Owner}'s previous region {PreviousRegionId} is not one the form's region task creates; it is kept",
                    owner.Label, previous);
                return null;
            }
            // A temp-named previous region is removed: whoever else stands on it keeps it. The row's own final name is
            // redefined in place: only a domain, a sub-region or another lootbox area holding it keeps it (as before #945);
            // a landmark or production area that names it just follows the new shape.
            bool removed = !previous.Equals(owner.FinalName, StringComparison.OrdinalIgnoreCase);
            string? takenBy = removed
                ? await OtherUserOfRegionAsync(previous, owner.World, owner.Self)
                : await RegionHolderAsync(previous, owner.World, owner.Self);
            if (takenBy != null)
            {
                _logger.LogWarning("{Owner}'s previous region {PreviousRegionId} is kept: {TakenBy}", owner.Label, previous, takenBy);
                return null;
            }
            return previous;
        }

        /// <summary>
        /// #945: who holds <paramref name="regionId"/> so that it may not be redefined under them: a domain, a domain
        /// sub-region or a lootbox spawn area other than <paramref name="self"/>; or null. Landmarks and production areas
        /// only read a region's shape, so they never hold one.
        /// </summary>
        private async Task<string?> RegionHolderAsync(string regionId, string? world, RegionOwnerRef self)
        {
            string? takenBy = await DomainRegionOwnership.TakenByAsync(_context, regionId, world);
            int exceptArea = self.Kind == RegionOwnerKind.LootboxSpawnArea ? self.Id : 0;
            if (takenBy == null && await LootboxAreaUsingAsync(regionId, world, exceptArea) is int areaId)
            {
                takenBy = $"lootbox spawn area {areaId} stands on it";
            }
            return takenBy;
        }

        /// <summary>#34: <see cref="ReplaceableOwnedRegionAsync"/> for a lootbox spawn area.</summary>
        public Task<string?> ReplaceableLootboxAreaRegionAsync(LootboxSpawnArea area, string? previousRegionId, string finalName, string? world) =>
            ReplaceableOwnedRegionAsync(LootboxAreaOwner(area, finalName, world), previousRegionId);

        /// <summary>#945: <see cref="ReplaceableOwnedRegionAsync"/> for a landmark.</summary>
        public Task<string?> ReplaceableLandmarkRegionAsync(Landmark landmark, string? previousRegionId, string finalName, string? world) =>
            ReplaceableOwnedRegionAsync(LandmarkOwner(landmark, finalName, world), previousRegionId);

        /// <summary>#946: <see cref="ReplaceableOwnedRegionAsync"/> for a production area.</summary>
        public Task<string?> ReplaceableProductionAreaRegionAsync(ProductionArea area, string? previousRegionId, string finalName, string? world) =>
            ReplaceableOwnedRegionAsync(ProductionAreaOwner(area, finalName, world), previousRegionId);

        /// <summary>#945: the world of the landmark's pin (its Location), or null when it has none.</summary>
        private async Task<string?> LandmarkWorldAsync(Landmark landmark)
        {
            string? world = landmark.Location?.World;
            if (string.IsNullOrWhiteSpace(world) && landmark.LocationId > 0)
            {
                world = await _context.Locations.AsNoTracking()
                    .Where(l => l.Id == landmark.LocationId)
                    .Select(l => l.World)
                    .FirstOrDefaultAsync();
            }
            return WorldOrNull(world);
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
                // A rural structure (P2, #91) has no parent region: no district, and territory is not a region.
                Structure structure => structure.DistrictId ?? 0,
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
