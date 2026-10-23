using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Who already uses a WorldGuard region id in a world (KNG-111, KNG-254): a region belongs to at most one domain, as
    /// its main region (<c>domains.WgRegionId</c>) or as one of its sub-regions (<c>domain_sub_regions</c>, in their
    /// domain's world). Case-insensitive, like WorldGuard. A null world matches every world.
    /// </summary>
    public static class DomainRegionOwnership
    {
        /// <summary>
        /// A message naming the domain (or domain sub-region) that holds <paramref name="regionId"/> in
        /// <paramref name="world"/>, or null when it is free. <paramref name="exceptDomainId"/> skips that domain's main
        /// region and <paramref name="exceptSubRegionId"/> that sub-region: the row being saved.
        /// </summary>
        public static async Task<string?> TakenByAsync(KnKDbContext context, string regionId, string? world,
            int? exceptDomainId = null, int? exceptSubRegionId = null)
        {
            string loweredRegion = regionId.Trim().ToLower();
            string? loweredWorld = string.IsNullOrWhiteSpace(world) ? null : world.Trim().ToLower();
            int selfDomain = exceptDomainId ?? 0;
            int selfSub = exceptSubRegionId ?? 0;

            var domain = await context.Domains.AsNoTracking()
                .Where(d => d.Id != selfDomain
                    && d.WgRegionId != null && d.WgRegionId.ToLower() == loweredRegion
                    && (loweredWorld == null || (d.WorldName != null && d.WorldName.ToLower() == loweredWorld)))
                .OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.Name })
                .FirstOrDefaultAsync();
            if (domain != null)
            {
                return $"Region '{regionId}'{WorldText(world)} already belongs to {domain.Name} (#{domain.Id}).";
            }

            var sub = await context.DomainSubRegions.AsNoTracking()
                .Where(s => s.Id != selfSub
                    && s.WgRegionId.ToLower() == loweredRegion
                    && (loweredWorld == null || (s.Domain.WorldName != null && s.Domain.WorldName.ToLower() == loweredWorld)))
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.DomainId, DomainName = s.Domain.Name })
                .FirstOrDefaultAsync();
            return sub == null
                ? null
                : $"Region '{regionId}'{WorldText(world)} is already sub-region #{sub.Id} of {sub.DomainName} (#{sub.DomainId}).";
        }

        private static string WorldText(string? world) => string.IsNullOrWhiteSpace(world) ? string.Empty : $" in world '{world}'";
    }
}
