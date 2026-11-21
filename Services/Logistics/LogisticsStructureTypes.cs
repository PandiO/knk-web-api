using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Logistics
{
    /// <summary>
    /// The most specific dashboard type of a structure (PandiO/knk-workspace#928 point 3), so the web app can link a
    /// consumer to its own page (a shop's or production structure's page has the consumer panel; the generic Structure
    /// page has not). Structures are TPT: Shop is a ProductionStructure, so Shop is checked first.
    /// </summary>
    public static class LogisticsStructureTypes
    {
        public const string Shop = "Shop";
        public const string ProductionStructure = "ProductionStructure";
        public const string Warehouse = "Warehouse";
        public const string GateStructure = "GateStructure";
        public const string Structure = "Structure";

        /// <summary>Structure id → its type name; unknown ids are left out.</summary>
        public static async Task<Dictionary<int, string>> ResolveAsync(KnKDbContext db, IEnumerable<int> ids, CancellationToken ct = default)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            var rows = await db.Structures.AsNoTracking().Where(s => list.Contains(s.Id))
                .Select(s => new
                {
                    s.Id,
                    Type = s is Models.Shop ? Shop
                        : s is Models.ProductionStructure ? ProductionStructure
                        : s is Models.Warehouse ? Warehouse
                        : s is Models.GateStructure ? GateStructure
                        : Structure,
                })
                .ToListAsync(ct);
            return rows.ToDictionary(r => r.Id, r => r.Type);
        }
    }
}
