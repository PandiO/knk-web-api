using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Stock
{
    /// <summary>What the reconciler found. <see cref="Clean"/> = no difference of any kind.</summary>
    public sealed class StockReconciliationReport
    {
        public const int MaxListed = 100;

        [JsonPropertyName("checkedAt")] public DateTime CheckedAt { get; set; }
        [JsonPropertyName("clean")] public bool Clean { get; set; }
        [JsonPropertyName("linesChecked")] public int LinesChecked { get; set; }
        [JsonPropertyName("unitsChecked")] public int UnitsChecked { get; set; }
        [JsonPropertyName("blueprintImbalanceCount")] public int BlueprintImbalanceCount { get; set; }
        [JsonPropertyName("lineDifferenceCount")] public int LineDifferenceCount { get; set; }
        [JsonPropertyName("unitDifferenceCount")] public int UnitDifferenceCount { get; set; }
        [JsonPropertyName("totalDifferenceCount")] public int TotalDifferenceCount { get; set; }

        /// <summary>Blueprints whose amounts over all storages (virtual ones included) don't sum to zero.</summary>
        [JsonPropertyName("blueprintImbalances")] public List<BlueprintImbalance> BlueprintImbalances { get; set; } = new();

        /// <summary>Stock lines whose amount differs from the net of their moves and roll-ups.</summary>
        [JsonPropertyName("lineDifferences")] public List<LineDifference> LineDifferences { get; set; } = new();

        /// <summary>Unique items whose storage differs from the target of their last move.</summary>
        [JsonPropertyName("unitDifferences")] public List<UnitDifference> UnitDifferences { get; set; } = new();

        /// <summary>Storages whose stored totals differ from their lines and units.</summary>
        [JsonPropertyName("totalDifferences")] public List<TotalDifference> TotalDifferences { get; set; } = new();

        public sealed record BlueprintImbalance(
            [property: JsonPropertyName("itemBlueprintId")] int ItemBlueprintId,
            [property: JsonPropertyName("sum")] long Sum);

        public sealed record LineDifference(
            [property: JsonPropertyName("storageId")] int StorageId,
            [property: JsonPropertyName("itemBlueprintId")] int ItemBlueprintId,
            [property: JsonPropertyName("lineAmount")] long LineAmount,
            [property: JsonPropertyName("ledgerNet")] long LedgerNet);

        public sealed record UnitDifference(
            [property: JsonPropertyName("itemInstanceId")] long ItemInstanceId,
            [property: JsonPropertyName("storageId")] int? StorageId,
            [property: JsonPropertyName("ledgerStorageId")] int? LedgerStorageId);

        public sealed record TotalDifference(
            [property: JsonPropertyName("storageId")] int StorageId,
            [property: JsonPropertyName("storedTotalUnits")] long StoredTotalUnits,
            [property: JsonPropertyName("actualTotalUnits")] long ActualTotalUnits,
            [property: JsonPropertyName("storedDistinctItems")] int StoredDistinctItems,
            [property: JsonPropertyName("actualDistinctItems")] int ActualDistinctItems);
    }

    public interface IStockReconciler
    {
        Task<StockReconciliationReport> RunAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// The stock reconciler (production DESIGN § 4.3): for every blueprint the sum over all storages is zero; every
    /// stock line equals the net of its moves plus its roll-up rows (G3); every unique item is where its last move put
    /// it; every storage's stored totals match its lines and units. Read-only: it reports, it never repairs (a repair is
    /// an admin adjustment with a note).
    /// </summary>
    public sealed class StockReconciler : IStockReconciler
    {
        private readonly KnKDbContext _db;
        private readonly TimeProvider _clock;

        public StockReconciler(KnKDbContext db, TimeProvider? clock = null)
        {
            _db = db;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<StockReconciliationReport> RunAsync(CancellationToken ct = default)
        {
            var report = new StockReconciliationReport { CheckedAt = _clock.GetUtcNow().UtcDateTime };

            var lines = await _db.StockLines.AsNoTracking()
                .Select(l => new { l.StorageId, l.ItemBlueprintId, l.Amount }).ToListAsync(ct);
            report.LinesChecked = lines.Count;

            // 1. Per blueprint the sum is zero.
            var imbalances = lines.GroupBy(l => l.ItemBlueprintId)
                .Select(g => new StockReconciliationReport.BlueprintImbalance(g.Key, g.Sum(l => l.Amount)))
                .Where(b => b.Sum != 0).OrderBy(b => b.ItemBlueprintId).ToList();
            report.BlueprintImbalanceCount = imbalances.Count;
            report.BlueprintImbalances = imbalances.Take(StockReconciliationReport.MaxListed).ToList();

            // 2. Each line equals the net of its moves and roll-ups.
            var net = new Dictionary<(int, int), long>();
            void Add(int storage, int blueprint, long amount)
            {
                net.TryGetValue((storage, blueprint), out var v);
                net[(storage, blueprint)] = v + amount;
            }
            var moves = _db.StockMoves.AsNoTracking().Where(m => m.ItemBlueprintId != null);
            foreach (var r in await moves.GroupBy(m => new { m.ToStorageId, m.ItemBlueprintId })
                         .Select(g => new { g.Key.ToStorageId, g.Key.ItemBlueprintId, Sum = g.Sum(m => m.Amount) }).ToListAsync(ct))
                Add(r.ToStorageId, r.ItemBlueprintId!.Value, r.Sum);
            foreach (var r in await moves.GroupBy(m => new { m.FromStorageId, m.ItemBlueprintId })
                         .Select(g => new { g.Key.FromStorageId, g.Key.ItemBlueprintId, Sum = g.Sum(m => m.Amount) }).ToListAsync(ct))
                Add(r.FromStorageId, r.ItemBlueprintId!.Value, -r.Sum);
            var rollups = _db.StockMoveRollups.AsNoTracking();
            foreach (var r in await rollups.GroupBy(m => new { m.ToStorageId, m.ItemBlueprintId })
                         .Select(g => new { g.Key.ToStorageId, g.Key.ItemBlueprintId, Sum = g.Sum(m => m.Amount) }).ToListAsync(ct))
                Add(r.ToStorageId, r.ItemBlueprintId, r.Sum);
            foreach (var r in await rollups.GroupBy(m => new { m.FromStorageId, m.ItemBlueprintId })
                         .Select(g => new { g.Key.FromStorageId, g.Key.ItemBlueprintId, Sum = g.Sum(m => m.Amount) }).ToListAsync(ct))
                Add(r.FromStorageId, r.ItemBlueprintId, -r.Sum);

            var lineAmounts = lines.ToDictionary(l => (l.StorageId, l.ItemBlueprintId), l => l.Amount);
            var lineDiffs = new List<StockReconciliationReport.LineDifference>();
            foreach (var key in lineAmounts.Keys.Union(net.Keys))
            {
                lineAmounts.TryGetValue(key, out var amount);
                net.TryGetValue(key, out var ledger);
                if (amount != ledger) lineDiffs.Add(new(key.Item1, key.Item2, amount, ledger));
            }
            lineDiffs = lineDiffs.OrderBy(d => d.StorageId).ThenBy(d => d.ItemBlueprintId).ToList();
            report.LineDifferenceCount = lineDiffs.Count;
            report.LineDifferences = lineDiffs.Take(StockReconciliationReport.MaxListed).ToList();

            // 3. Each unique item is where its last move put it (none = a virtual storage: in the world).
            var units = await _db.StockUnits.AsNoTracking().Select(u => new { u.ItemInstanceId, u.StorageId }).ToListAsync(ct);
            report.UnitsChecked = units.Count;
            var lastMoveIds = _db.StockMoves.Where(m => m.ItemInstanceId != null)
                .GroupBy(m => m.ItemInstanceId).Select(g => g.Max(m => m.Id));
            var lastMoves = await _db.StockMoves.AsNoTracking().Where(m => lastMoveIds.Contains(m.Id))
                .Select(m => new { InstanceId = m.ItemInstanceId!.Value, m.ToStorageId, Virtual = m.ToStorage.Kind == StorageKind.Virtual })
                .ToListAsync(ct);
            var ledgerAt = lastMoves.ToDictionary(m => m.InstanceId, m => m.Virtual ? (int?)null : m.ToStorageId);
            var unitAt = units.ToDictionary(u => u.ItemInstanceId, u => (int?)u.StorageId);
            var unitDiffs = new List<StockReconciliationReport.UnitDifference>();
            foreach (var id in unitAt.Keys.Union(ledgerAt.Keys))
            {
                unitAt.TryGetValue(id, out var at);
                ledgerAt.TryGetValue(id, out var ledger);
                if (at != ledger) unitDiffs.Add(new(id, at, ledger));
            }
            unitDiffs = unitDiffs.OrderBy(d => d.ItemInstanceId).ToList();
            report.UnitDifferenceCount = unitDiffs.Count;
            report.UnitDifferences = unitDiffs.Take(StockReconciliationReport.MaxListed).ToList();

            // 4. Stored totals match the lines and units.
            var storages = await _db.Storages.AsNoTracking()
                .Select(s => new { s.Id, s.TotalUnits, s.DistinctItems }).ToListAsync(ct);
            var lineTotals = lines.GroupBy(l => l.StorageId).ToDictionary(g => g.Key, g => (Sum: g.Sum(l => l.Amount), Count: g.Count()));
            var unitCounts = units.GroupBy(u => u.StorageId).ToDictionary(g => g.Key, g => g.Count());
            var totalDiffs = new List<StockReconciliationReport.TotalDifference>();
            foreach (var s in storages)
            {
                lineTotals.TryGetValue(s.Id, out var lt);
                unitCounts.TryGetValue(s.Id, out var uc);
                long total = lt.Sum + uc;
                int distinct = lt.Count + uc;
                if (total != s.TotalUnits || distinct != s.DistinctItems)
                    totalDiffs.Add(new(s.Id, s.TotalUnits, total, s.DistinctItems, distinct));
            }
            report.TotalDifferenceCount = totalDiffs.Count;
            report.TotalDifferences = totalDiffs.Take(StockReconciliationReport.MaxListed).ToList();

            report.Clean = report.BlueprintImbalanceCount == 0 && report.LineDifferenceCount == 0
                && report.UnitDifferenceCount == 0 && report.TotalDifferenceCount == 0;
            return report;
        }
    }
}
