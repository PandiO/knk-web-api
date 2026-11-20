using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    /// <summary>A structure as transport sees it: its effective town, whether it is rural, and its position.</summary>
    public sealed record TransportPlace(int Id, string Name, int? TownId, bool IsRural, string? World, double? X, double? Y, double? Z)
    {
        public bool HasPosition => X.HasValue && Z.HasValue;
    }

    /// <summary>One leg's length in blocks and where the number came from.</summary>
    public sealed record TransportDistance(decimal Blocks, TransportDistanceSource Source)
    {
        public long Seconds(decimal speed) => speed <= 0 ? 0 : (long)Math.Ceiling(Blocks / speed);
    }

    /// <summary>
    /// Routes and room (production DESIGN § 7.6, G14): a leg is the measured lane's road length when there is a
    /// reachable lane (either direction), else the straight distance between the two structures × the settings' factor,
    /// else the fallback distance. The simulated trip takes the leg ÷ the walking speed.
    /// </summary>
    public sealed class TransportRouting
    {
        private readonly KnKDbContext _db;

        public TransportRouting(KnKDbContext db) => _db = db;

        public async Task<TransportPlace?> PlaceAsync(int? structureId, CancellationToken ct = default)
        {
            if (structureId == null) return null;
            return await _db.Structures.AsNoTracking().Where(s => s.Id == structureId)
                .Select(s => new TransportPlace(s.Id, s.Name, s.District != null ? s.District.TownId : s.TownId, s.DistrictId == null,
                    s.Location != null ? s.Location.World : null,
                    s.Location != null ? s.Location.X : null, s.Location != null ? s.Location.Y : null,
                    s.Location != null ? s.Location.Z : null))
                .FirstOrDefaultAsync(ct);
        }

        public async Task<TransportDistance> DistanceAsync(int? from, int? to, ProductionTransportSettingsDto settings,
            CancellationToken ct = default)
        {
            if (from.HasValue && to.HasValue)
            {
                if (from == to) return new TransportDistance(0m, TransportDistanceSource.Lane);
                var lanes = await _db.TransportLanes.AsNoTracking()
                    .Where(l => (l.FromStructureId == from && l.ToStructureId == to) || (l.FromStructureId == to && l.ToStructureId == from))
                    .ToListAsync(ct);
                var lane = lanes.Where(l => l.Reachable && l.RoadDistance.HasValue)
                    .OrderBy(l => l.FromStructureId == from ? 0 : 1).FirstOrDefault();
                if (lane != null) return new TransportDistance(lane.RoadDistance!.Value, TransportDistanceSource.Lane);
            }
            var a = await PlaceAsync(from, ct);
            var b = await PlaceAsync(to, ct);
            if (a is { HasPosition: true } && b is { HasPosition: true }
                && string.Equals(a.World ?? "world", b.World ?? "world", StringComparison.OrdinalIgnoreCase))
            {
                double dx = a.X!.Value - b.X!.Value, dz = a.Z!.Value - b.Z!.Value;
                decimal straight = (decimal)Math.Sqrt(dx * dx + dz * dz);
                return new TransportDistance(Math.Round(straight * settings.UnmeasuredRouteFactor, 2), TransportDistanceSource.Estimated);
            }
            return new TransportDistance(settings.FallbackDistanceBlocks, TransportDistanceSource.Fallback);
        }

        /// <summary>The structure that holds a storage (a Domain storage whose domain is a structure), else null.</summary>
        public async Task<int?> StructureOfStorageAsync(int storageId, CancellationToken ct = default)
        {
            var domainId = await _db.Storages.AsNoTracking().Where(s => s.Id == storageId && s.Kind == StorageKind.Domain)
                .Select(s => s.DomainId).FirstOrDefaultAsync(ct);
            if (domainId == null) return null;
            return await _db.Structures.AsNoTracking().AnyAsync(s => s.Id == domainId, ct) ? domainId : null;
        }

        /// <summary>Raw units (× storage weight) a storage holds now.</summary>
        public async Task<decimal> UsedUnitsAsync(int storageId, CancellationToken ct = default)
        {
            decimal lines = await _db.StockLines.AsNoTracking().Where(l => l.StorageId == storageId)
                .SumAsync(l => (decimal?)(l.Amount * l.ItemBlueprint.StorageWeight), ct) ?? 0m;
            decimal units = await _db.StockUnits.AsNoTracking().Where(u => u.StorageId == storageId)
                .SumAsync(u => (decimal?)u.ItemInstance.ItemBlueprint.StorageWeight, ct) ?? 0m;
            return lines + units;
        }

        /// <summary>Raw units the storage can still take (null = uncapped; never below 0).</summary>
        public async Task<decimal?> RoomAsync(int storageId, CancellationToken ct = default)
        {
            var s = await _db.Storages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == storageId, ct);
            if (s == null || !s.IsCapped || !s.MaxUnits.HasValue) return null;
            return Math.Max(0m, s.MaxUnits.Value - await UsedUnitsAsync(storageId, ct));
        }

        public async Task<Dictionary<int, decimal>> WeightsAsync(IEnumerable<int> blueprintIds, CancellationToken ct = default)
        {
            var ids = blueprintIds.Distinct().ToList();
            var rows = await _db.ItemBlueprints.AsNoTracking().Where(b => ids.Contains(b.Id))
                .Select(b => new { b.Id, b.StorageWeight }).ToListAsync(ct);
            return rows.ToDictionary(r => r.Id, r => r.StorageWeight > 0 ? r.StorageWeight : ItemBlueprint.DefaultStorageWeight);
        }

        /// <summary>The warehouses of a town that take deliveries and have a delivery storage.</summary>
        public Task<List<Warehouse>> WarehousesOfTownAsync(int townId, CancellationToken ct = default) =>
            _db.Warehouses.AsNoTracking().Include(w => w.District)
                .Where(w => w.AcceptsDeliveries && w.DefaultStorageId != null
                    && (w.TownId == townId || (w.District != null && w.District.TownId == townId)))
                .OrderBy(w => w.Id).ToListAsync(ct);
    }
}
