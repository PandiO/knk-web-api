using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// What a domain create/update knows about the domain's world (KNG-111). Every field is optional; the resolver
    /// combines them.
    /// </summary>
    public sealed class DomainWorldRequest
    {
        /// <summary>The domain being updated; null on create.</summary>
        public int? DomainId { get; init; }

        /// <summary>A world chosen in the form (or sent by a client that knows it).</summary>
        public string? RequestedWorld { get; init; }

        public string? WgRegionId { get; init; }

        /// <summary>The world of a Location embedded in the request (it may not be saved yet).</summary>
        public string? LocationWorld { get; init; }

        /// <summary>A saved Location the domain points at; used when no embedded Location gives a world.</summary>
        public int? LocationId { get; init; }

        /// <summary>The parent domain: a District's Town, a Structure's (or GateStructure's) District.</summary>
        public int? ParentDomainId { get; init; }
    }

    /// <summary>The outcome of <see cref="IDomainWorldResolver.TryResolveAsync"/>.</summary>
    public sealed record DomainWorldResolution(string? WorldName, string? Source, string? Error, string? ErrorCode)
    {
        /// <summary>No source gave a world and nothing conflicts: the form has to ask for it.</summary>
        public bool NeedsWorld => WorldName == null && Error == null;
    }

    /// <summary>
    /// Thrown by <see cref="IDomainWorldResolver.ResolveAsync"/>. An <see cref="ArgumentException"/>, so the domain
    /// controllers already answer it with 400 and the message.
    /// </summary>
    public class DomainWorldException : ArgumentException
    {
        public const string WorldRequired = "DomainWorldRequired";
        public const string WorldConflict = "DomainWorldConflict";
        public const string RegionTaken = "DomainRegionTaken";

        public string Code { get; }

        public DomainWorldException(string code, string message) : base(message)
        {
            Code = code;
        }
    }

    /// <summary>
    /// Decides which Minecraft world a domain is in and checks that it fits (KNG-111, knk-workspace
    /// docs/reports/2026-10-10-multiworld-capability-audit.md §4). Sources, in order:
    /// <list type="number">
    /// <item>the world requested by the form;</item>
    /// <item>the world the region was drawn in: a <c>tempregion_worldtask_&lt;taskId&gt;</c> region names its
    /// WgRegionId world task, whose output carries <c>worldName</c>;</item>
    /// <item>the world of the domain's Location (embedded or saved);</item>
    /// <item>the parent domain's world;</item>
    /// <item>on update, the world the domain already has.</item>
    /// </list>
    /// Every source that gives a world must agree, a domain with children keeps them in its world, and a region id is
    /// used by at most one domain per world.
    /// </summary>
    public interface IDomainWorldResolver
    {
        /// <summary>Resolves and validates the world, or throws <see cref="DomainWorldException"/>.</summary>
        Task<string> ResolveAsync(DomainWorldRequest request);

        /// <summary>The same checks without throwing: lets the web form ask for a world before submitting.</summary>
        Task<DomainWorldResolution> TryResolveAsync(DomainWorldRequest request);
    }

    public class DomainWorldResolver : IDomainWorldResolver
    {
        public const int MaxWorldNameLength = 64;

        private const string RequestedSource = "form";
        private const string RegionTaskSource = "region world task";
        private const string LocationSource = "location";
        private const string ParentSource = "parent domain";
        private const string ExistingSource = "saved domain";

        private readonly KnKDbContext _context;

        public DomainWorldResolver(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<string> ResolveAsync(DomainWorldRequest request)
        {
            var resolution = await TryResolveAsync(request);
            if (resolution.Error != null)
            {
                throw new DomainWorldException(resolution.ErrorCode ?? DomainWorldException.WorldConflict, resolution.Error);
            }
            if (resolution.WorldName == null)
            {
                throw new DomainWorldException(DomainWorldException.WorldRequired,
                    "The domain's world is unknown: capture its region or location in game, or choose the world in the form.");
            }
            return resolution.WorldName;
        }

        public async Task<DomainWorldResolution> TryResolveAsync(DomainWorldRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            string? requested = Normalize(request.RequestedWorld);
            if (requested != null && requested.Length > MaxWorldNameLength)
            {
                return Conflict(DomainWorldException.WorldConflict,
                    $"World name '{requested}' is longer than {MaxWorldNameLength} characters.");
            }

            var existing = request.DomainId is int domainId && domainId > 0
                ? await _context.Domains.AsNoTracking()
                    .Where(d => d.Id == domainId)
                    .Select(d => new { d.WgRegionId, d.WorldName })
                    .FirstOrDefaultAsync()
                : null;

            // Only the sources that pin the world; the saved world is a fallback, so changing it on purpose works.
            var sources = new List<(string Source, string World)>();
            AddSource(sources, RequestedSource, requested);
            AddSource(sources, RegionTaskSource, await RegionTaskWorldAsync(request.WgRegionId));
            AddSource(sources, LocationSource, await LocationWorldAsync(request));
            AddSource(sources, ParentSource, await DomainWorldAsync(request.ParentDomainId));

            string? world = sources.Count > 0 ? sources[0].World : Normalize(existing?.WorldName);
            string? source = sources.Count > 0 ? sources[0].Source : world != null ? ExistingSource : null;

            var disagreeing = sources.Where(s => !SameWorld(s.World, world)).ToList();
            if (disagreeing.Count > 0)
            {
                string described = string.Join(", ", sources.Select(s => $"{s.Source} '{s.World}'"));
                return Conflict(DomainWorldException.WorldConflict,
                    $"The domain's world is ambiguous: {described}. A domain, its region, its location and its parent must all be in one world.");
            }

            if (world == null)
            {
                return new DomainWorldResolution(null, null, null, null);
            }

            if (request.DomainId is int id && id > 0)
            {
                string? childConflict = await ChildWorldConflictAsync(id, world);
                if (childConflict != null)
                {
                    return Conflict(DomainWorldException.WorldConflict, childConflict);
                }
            }

            string? regionId = Normalize(request.WgRegionId);
            bool regionOrWorldChanged = existing == null
                || !string.Equals(Normalize(existing.WgRegionId), regionId, StringComparison.OrdinalIgnoreCase)
                || !SameWorld(Normalize(existing.WorldName), world);
            if (regionId != null && regionOrWorldChanged)
            {
                string? taken = await RegionTakenAsync(request.DomainId, regionId, world);
                if (taken != null)
                {
                    return Conflict(DomainWorldException.RegionTaken, taken);
                }
            }

            return new DomainWorldResolution(world, source, null, null);
        }

        /// <summary>The world in a WgRegionId world task's output, for a region still named after its task.</summary>
        private async Task<string?> RegionTaskWorldAsync(string? wgRegionId)
        {
            string? regionId = Normalize(wgRegionId);
            if (regionId == null || !DomainRegionNameFinalizer.IsTemporary(regionId))
            {
                return null;
            }
            string suffix = regionId.Substring(DomainRegionNameFinalizer.TempRegionPrefix.Length);
            if (!int.TryParse(suffix, out int taskId) || taskId <= 0)
            {
                return null;
            }
            string? outputJson = await _context.WorldTasks.AsNoTracking()
                .Where(t => t.Id == taskId)
                .Select(t => t.OutputJson)
                .FirstOrDefaultAsync();
            return WorldNameFromTaskOutput(outputJson);
        }

        /// <summary>Reads <c>worldName</c> (any case) from a world task's output JSON.</summary>
        public static string? WorldNameFromTaskOutput(string? outputJson)
        {
            if (string.IsNullOrWhiteSpace(outputJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(outputJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "worldName", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        return Normalize(property.Value.GetString());
                    }
                }
            }
            catch (JsonException)
            {
                // A malformed output gives no world; another source may.
            }
            return null;
        }

        private async Task<string?> LocationWorldAsync(DomainWorldRequest request)
        {
            string? embedded = Normalize(request.LocationWorld);
            if (embedded != null) return embedded;
            if (request.LocationId is not int locationId || locationId <= 0) return null;
            string? world = await _context.Locations.AsNoTracking()
                .Where(l => l.Id == locationId)
                .Select(l => l.World)
                .FirstOrDefaultAsync();
            return Normalize(world);
        }

        private async Task<string?> DomainWorldAsync(int? domainId)
        {
            if (domainId is not int id || id <= 0) return null;
            string? world = await _context.Domains.AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => d.WorldName)
                .FirstOrDefaultAsync();
            return Normalize(world);
        }

        private async Task<string?> ChildWorldConflictAsync(int domainId, string world)
        {
            string lowered = world.ToLower();
            var districts = await _context.Districts.AsNoTracking()
                .Where(d => d.TownId == domainId && d.WorldName != null && d.WorldName != "" && d.WorldName.ToLower() != lowered)
                .Select(d => new { d.Id, d.Name, d.WorldName })
                .ToListAsync();
            var structures = await _context.Structures.AsNoTracking()
                .Where(s => s.DistrictId == domainId && s.WorldName != null && s.WorldName != "" && s.WorldName.ToLower() != lowered)
                .Select(s => new { s.Id, s.Name, s.WorldName })
                .ToListAsync();
            var children = districts.Concat(structures).ToList();
            if (children.Count == 0) return null;
            string listed = string.Join(", ", children.Take(5).Select(c => $"{c.Name} (#{c.Id}, world '{c.WorldName}')"));
            return $"The domain can't move to world '{world}': its children are in another world: {listed}.";
        }

        private async Task<string?> RegionTakenAsync(int? domainId, string regionId, string world)
        {
            string loweredRegion = regionId.ToLower();
            string loweredWorld = world.ToLower();
            int self = domainId ?? 0;
            var other = await _context.Domains.AsNoTracking()
                .Where(d => d.Id != self
                    && d.WgRegionId != null && d.WgRegionId.ToLower() == loweredRegion
                    && d.WorldName != null && d.WorldName.ToLower() == loweredWorld)
                .OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.Name })
                .FirstOrDefaultAsync();
            return other == null
                ? null
                : $"Region '{regionId}' in world '{world}' already belongs to {other.Name} (#{other.Id}).";
        }

        private static void AddSource(List<(string Source, string World)> sources, string source, string? world)
        {
            if (world != null) sources.Add((source, world));
        }

        private static DomainWorldResolution Conflict(string code, string message) => new(null, null, message, code);

        private static bool SameWorld(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string? Normalize(string? value)
        {
            string? trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}
