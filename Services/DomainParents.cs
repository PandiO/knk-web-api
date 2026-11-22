using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The one parent walk of the domain hierarchy (landmark-domain DESIGN LD23, PandiO/knk-workspace#983, #981). Each
    /// domain type names its parent here and nowhere else: a district its town, a structure (and every structure subtype)
    /// its district, a landmark its <see cref="Landmark.ParentDomain"/> (any domain, another landmark too). A town, a
    /// rural structure (no district, P2 #91), a crown land, a bare domain and a top-level landmark have none. Every walk
    /// ends: a domain seen before (a cycle) or more than <see cref="MaxDepth"/> steps stop it. Used by the region decisions
    /// (<c>DomainMappingProfile</c>), the repository's parent loading (<c>DomainRepository.GetByAnyRegionAsync</c>), the
    /// region name finalizer (parent region, order of the finalize pass) and the landmark parent rules.
    /// </summary>
    public static class DomainParents
    {
        /// <summary>No real hierarchy is this deep; a longer chain is a data error and ends the walk.</summary>
        public const int MaxDepth = 32;

        /// <summary>The parent's id as the domain stores it, or null when its type has no parent or it is top level.</summary>
        public static int? ParentIdOf(Domain domain) => domain switch
        {
            District district => district.TownId > 0 ? district.TownId : null,
            Structure structure => structure.DistrictId is > 0 ? structure.DistrictId : null,
            Landmark landmark => landmark.ParentDomainId is > 0 ? landmark.ParentDomainId : null,
            _ => null
        };

        /// <summary>The parent when it is loaded on the entity; null when there is none or it is not loaded.</summary>
        public static Domain? LoadedParentOf(Domain domain) => ParentIdOf(domain) == null ? null : domain switch
        {
            District district => district.Town,
            Structure structure => structure.District,
            Landmark landmark => landmark.ParentDomain,
            _ => null
        };

        /// <summary>
        /// The loaded ancestors of <paramref name="domain"/>, nearest first. Stops at the first parent that is not loaded,
        /// at a domain already seen (a cycle never repeats) and after <see cref="MaxDepth"/> steps.
        /// </summary>
        public static IReadOnlyList<Domain> LoadedAncestors(Domain domain)
        {
            var ancestors = new List<Domain>();
            if (domain == null) return ancestors;
            var seen = new HashSet<int> { domain.Id };
            var current = LoadedParentOf(domain);
            while (current != null && ancestors.Count < MaxDepth && seen.Add(current.Id))
            {
                ancestors.Add(current);
                current = LoadedParentOf(current);
            }
            return ancestors;
        }

        /// <summary>
        /// The ancestor ids of <paramref name="domainId"/>, nearest first, from a parent lookup; the same guards as
        /// <see cref="LoadedAncestors"/>. A chain that comes back to <paramref name="domainId"/> stops before it.
        /// </summary>
        public static IReadOnlyList<int> AncestorIds(int domainId, Func<int, int?> parentOf)
        {
            var ancestors = new List<int>();
            var seen = new HashSet<int> { domainId };
            var current = parentOf(domainId);
            while (current is int id && ancestors.Count < MaxDepth && seen.Add(id))
            {
                ancestors.Add(id);
                current = parentOf(id);
            }
            return ancestors;
        }

        /// <summary>The number of ancestors, as <see cref="AncestorIds"/> counts them.</summary>
        public static int Depth(int domainId, Func<int, int?> parentOf) => AncestorIds(domainId, parentOf).Count;

        /// <summary>The stored parent id of every domain that has one (districts, structures, landmarks), by domain id.</summary>
        public static async Task<Dictionary<int, int?>> ParentIdMapAsync(KnKDbContext context)
        {
            var map = new Dictionary<int, int?>();
            foreach (var row in await context.Districts.AsNoTracking().Select(d => new { d.Id, d.TownId }).ToListAsync())
                map[row.Id] = row.TownId > 0 ? row.TownId : null;
            foreach (var row in await context.Structures.AsNoTracking().Select(s => new { s.Id, s.DistrictId }).ToListAsync())
                map[row.Id] = row.DistrictId is > 0 ? row.DistrictId : null;
            foreach (var row in await context.Landmarks.AsNoTracking().Select(l => new { l.Id, l.ParentDomainId }).ToListAsync())
                map[row.Id] = row.ParentDomainId is > 0 ? row.ParentDomainId : null;
            return map;
        }

        /// <summary>
        /// Loads the parent chain of <paramref name="domain"/> onto the tracked entities, one reference per step (the
        /// repository's parent loading). Same guards as <see cref="LoadedAncestors"/>.
        /// </summary>
        public static async Task LoadAncestorsAsync(KnKDbContext context, Domain domain)
        {
            var seen = new HashSet<int> { domain.Id };
            Domain? current = domain;
            for (int depth = 0; current != null && depth < MaxDepth; depth++)
            {
                if (ParentIdOf(current) == null) return;
                switch (current)
                {
                    case District district:
                        await context.Entry(district).Reference(d => d.Town).LoadAsync();
                        break;
                    case Structure structure:
                        await context.Entry(structure).Reference(s => s.District).LoadAsync();
                        break;
                    case Landmark landmark:
                        await context.Entry(landmark).Reference(l => l.ParentDomain).LoadAsync();
                        break;
                }
                current = LoadedParentOf(current);
                if (current == null || !seen.Add(current.Id)) return;
            }
        }
    }
}
