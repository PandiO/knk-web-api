using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Production;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Logistics
{
    /// <summary>The rules that apply to a town now: its current policy (else the current default template) plus the
    /// current policies of its warehouses, each rule active at the time (TOWN_LOGISTICS § 4).</summary>
    public sealed class LogisticsRuleSet
    {
        public LogisticsPolicy? Policy { get; init; }

        /// <summary>Town (the town's own policy), DefaultTemplate or None.</summary>
        public string Source { get; init; } = "None";

        public List<LogisticsRule> Rules { get; init; } = new();

        public IEnumerable<LogisticsRule> Of(LogisticsRuleType type) => Rules.Where(r => r.Type == type);

        public static async Task<LogisticsRuleSet> LoadAsync(KnKDbContext db, int townId, DateTime now, CancellationToken ct = default)
        {
            string key = LogisticsPolicy.KeyFor(LogisticsPolicyScope.Town, townId, null, 0);
            var policy = await db.Set<LogisticsPolicy>().AsNoTracking().Include(p => p.Rules).FirstOrDefaultAsync(p => p.CurrentKey == key, ct);
            string source = "Town";
            if (policy == null)
            {
                policy = await db.Set<LogisticsPolicy>().AsNoTracking().Include(p => p.Rules)
                    .Where(p => p.Scope == LogisticsPolicyScope.Template && p.IsDefaultTemplate && p.CurrentKey != null)
                    .OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
                source = policy == null ? "None" : "DefaultTemplate";
            }
            var warehouseIds = await db.Warehouses.AsNoTracking()
                .Where(w => w.TownId == townId || (w.District != null && w.District.TownId == townId)).Select(w => w.Id).ToListAsync(ct);
            var local = await db.Set<LogisticsPolicy>().AsNoTracking().Include(p => p.Rules)
                .Where(p => p.Scope == LogisticsPolicyScope.Warehouse && p.CurrentKey != null && p.WarehouseId != null && warehouseIds.Contains(p.WarehouseId.Value))
                .ToListAsync(ct);
            var rules = (policy?.Rules ?? new List<LogisticsRule>()).Concat(local.SelectMany(p => p.Rules))
                .Where(r => r.IsActiveAt(now)).OrderBy(r => r.LogisticsPolicyId == policy?.Id ? 0 : 1).ThenBy(r => r.Sequence).ThenBy(r => r.Id).ToList();
            foreach (var r in rules) r.LogisticsPolicy = null!;
            return new LogisticsRuleSet { Policy = policy, Source = source, Rules = rules };
        }

        /// <summary>The service area warehouse of a structure (TOWN_LOGISTICS § 2): a Structure rule, else a District rule
        /// for its district, else a Rural rule for a rural structure; null = nearest by road.</summary>
        public LogisticsRule? ServiceAreaFor(int structureId, int? districtId) =>
            Of(LogisticsRuleType.ServiceArea).FirstOrDefault(r => r.AreaKind == LogisticsAreaKind.Structure && r.StructureId == structureId)
            ?? (districtId.HasValue
                ? Of(LogisticsRuleType.ServiceArea).FirstOrDefault(r => r.AreaKind == LogisticsAreaKind.District && r.DistrictId == districtId)
                : Of(LogisticsRuleType.ServiceArea).FirstOrDefault(r => r.AreaKind == LogisticsAreaKind.Rural));
    }

    /// <summary>A blueprint's category chain and tags, for rule targets.</summary>
    public sealed record LogisticsItem(int Id, string Name, decimal Weight, IReadOnlySet<int> CategoryIds, IReadOnlySet<int> TagIds);

    /// <summary>A structure as the planner sees it.</summary>
    public sealed record LogisticsPlace(int Id, string Name, int? DistrictId, bool IsProduction, int? ProductionKindId, int WorkersTotal,
        int WorkersPresent, int? TransportCapacity, RoadConnectionState? RoadConnection);

    /// <summary>
    /// What the planner and the town views read about one town (TOWN_LOGISTICS § 2-5): its warehouses with their storages
    /// and stock, its consumer storages (Input or Merchandise storages of its structures, not warehouses), open
    /// reservations, what is on the way to consumers, outflow and inflow over the consumption window, rules, items.
    /// </summary>
    public sealed class LogisticsTownState
    {
        public int TownId { get; init; }
        public string TownName { get; init; } = string.Empty;
        public DateTime Now { get; init; }
        public LogisticsRuleSet RuleSet { get; init; } = new();
        public List<Warehouse> Warehouses { get; init; } = new();
        /// <summary>Storage id → warehouse id, for every storage a warehouse holds.</summary>
        public Dictionary<int, int> WarehouseOfStorage { get; init; } = new();
        public Dictionary<int, string> StorageNames { get; init; } = new();
        /// <summary>(storage, blueprint) → amount, for warehouse and consumer storages.</summary>
        public Dictionary<(int Storage, int Blueprint), long> Stock { get; init; } = new();
        public Dictionary<int, LogisticsPlace> Places { get; init; } = new();
        /// <summary>Consumer storages: (storage id, structure id, purpose).</summary>
        public List<(int StorageId, int StructureId, StoragePurpose Purpose)> ConsumerStorages { get; init; } = new();
        public List<LogisticsReservation> OpenReservations { get; init; } = new();
        /// <summary>Units on the way to consumer storages in open collection orders, per order.</summary>
        public List<(int OrderId, int Storage, int Blueprint, long Left)> InboundLines { get; init; } = new();

        /// <summary>Orders the planner released this run (their reservations expired): they don't count as on the way.</summary>
        public HashSet<int> DroppedOrders { get; } = new();

        /// <summary>(consumer storage, blueprint) → units on the way in open collection orders.</summary>
        public long InboundOf(int storage, int blueprint) =>
            InboundLines.Where(l => l.Storage == storage && l.Blueprint == blueprint && !DroppedOrders.Contains(l.OrderId)).Sum(l => Math.Max(0L, l.Left));
        /// <summary>(warehouse id, blueprint) → units that left the town's warehouses over the window, and came in.</summary>
        public Dictionary<(int Warehouse, int Blueprint), long> Outflow { get; init; } = new();
        public Dictionary<(int Warehouse, int Blueprint), long> Inflow { get; init; } = new();
        public int WindowDays { get; init; }
        public Dictionary<int, LogisticsItem> Items { get; init; } = new();
        public List<ConsumerReplenishment> Settings { get; init; } = new();
        /// <summary>Consumer structure → blueprint → input units per hour (enabled production lines).</summary>
        public Dictionary<int, Dictionary<int, decimal>> InputRates { get; init; } = new();

        public decimal DailyOutflowTown(int blueprint) =>
            WindowDays <= 0 ? 0m : (decimal)Outflow.Where(o => o.Key.Blueprint == blueprint).Sum(o => o.Value) / WindowDays;

        public decimal DailyOutflow(int warehouse, int blueprint) =>
            WindowDays <= 0 ? 0m : (decimal)Outflow.GetValueOrDefault((warehouse, blueprint)) / WindowDays;

        public long WarehouseStock(int warehouse, int blueprint) =>
            Stock.Where(s => s.Key.Blueprint == blueprint && WarehouseOfStorage.TryGetValue(s.Key.Storage, out var w) && w == warehouse).Sum(s => s.Value);

        public long TownStock(int blueprint) =>
            Stock.Where(s => s.Key.Blueprint == blueprint && WarehouseOfStorage.ContainsKey(s.Key.Storage)).Sum(s => s.Value);

        public IEnumerable<int> WarehouseBlueprints() =>
            Stock.Where(s => WarehouseOfStorage.ContainsKey(s.Key.Storage) && s.Value > 0).Select(s => s.Key.Blueprint).Distinct();

        public static async Task<LogisticsTownState> LoadAsync(KnKDbContext db, int townId, DateTime now, int windowDays, CancellationToken ct = default)
        {
            var town = await db.Towns.AsNoTracking().Where(t => t.Id == townId).Select(t => new { t.Id, t.Name }).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"Town {townId} not found.");
            var ruleSet = await LogisticsRuleSet.LoadAsync(db, townId, now, ct);
            var warehouses = await db.Warehouses.AsNoTracking().Include(w => w.District)
                .Where(w => w.TownId == townId || (w.District != null && w.District.TownId == townId)).OrderBy(w => w.Id).ToListAsync(ct);
            var warehouseIds = warehouses.Select(w => w.Id).ToList();

            var structures = await db.Structures.AsNoTracking()
                .Where(s => s.TownId == townId || (s.District != null && s.District.TownId == townId))
                .Select(s => new { s.Id, s.Name, s.DistrictId }).ToListAsync(ct);
            var structureIds = structures.Select(s => s.Id).ToList();
            var producers = await db.ProductionStructures.AsNoTracking().Where(p => structureIds.Contains(p.Id))
                .Select(p => new { p.Id, p.ProductionKindId, p.WorkersTotal, p.WorkersPresent, p.TransportCapacity, p.RoadConnection })
                .ToDictionaryAsync(p => p.Id, ct);
            var places = structures.ToDictionary(s => s.Id, s =>
            {
                var p = producers.GetValueOrDefault(s.Id);
                return new LogisticsPlace(s.Id, s.Name, s.DistrictId, p != null, p?.ProductionKindId, p?.WorkersTotal ?? 0, p?.WorkersPresent ?? 0,
                    p?.TransportCapacity, p?.RoadConnection);
            });

            var storages = await db.Storages.AsNoTracking()
                .Where(s => s.Kind == StorageKind.Domain && s.DomainId != null && structureIds.Contains(s.DomainId.Value))
                .Select(s => new { s.Id, s.Name, DomainId = s.DomainId!.Value, s.Purpose }).ToListAsync(ct);
            var warehouseOf = storages.Where(s => warehouseIds.Contains(s.DomainId)).ToDictionary(s => s.Id, s => s.DomainId);
            var consumerStorages = storages
                .Where(s => !warehouseIds.Contains(s.DomainId) && s.Purpose is StoragePurpose.Input or StoragePurpose.Merchandise)
                .OrderBy(s => s.Id).Select(s => (s.Id, s.DomainId, s.Purpose)).ToList();
            var relevant = warehouseOf.Keys.Concat(consumerStorages.Select(c => c.Id)).ToList();
            var stock = (await db.StockLines.AsNoTracking().Where(l => relevant.Contains(l.StorageId) && l.Amount > 0)
                    .Select(l => new { l.StorageId, l.ItemBlueprintId, l.Amount }).ToListAsync(ct))
                .ToDictionary(l => (l.StorageId, l.ItemBlueprintId), l => l.Amount);

            var open = await db.Set<LogisticsReservation>().AsNoTracking()
                .Where(r => r.TownId == townId && r.Status == LogisticsReservationStatus.Open).OrderBy(r => r.Id).ToListAsync(ct);

            var consumerIds = consumerStorages.Select(c => c.Id).ToList();
            var inboundRows = await db.TransportStopLines.AsNoTracking()
                .Where(l => consumerIds.Contains(l.TransportStop.StorageId) && l.TransportStop.TransportOrder.Origin == TransportOrigin.Distribution
                    && l.TransportStop.TransportOrder.NextActionAt != null)
                .Select(l => new { l.TransportStop.TransportOrderId, l.TransportStop.StorageId, l.ItemBlueprintId, Left = l.Planned - l.Delivered }).ToListAsync(ct);
            var inbound = inboundRows.Select(r => (r.TransportOrderId, r.StorageId, r.ItemBlueprintId, r.Left)).ToList();

            var since = now.AddDays(-windowDays);
            var whStorageIds = warehouseOf.Keys.ToList();
            var outRows = await db.StockMoves.AsNoTracking()
                .Where(m => whStorageIds.Contains(m.FromStorageId) && !whStorageIds.Contains(m.ToStorageId) && m.CreatedAt >= since && m.ItemBlueprintId != null)
                .Select(m => new { m.FromStorageId, Blueprint = m.ItemBlueprintId!.Value, m.Amount }).ToListAsync(ct);
            var inRows = await db.StockMoves.AsNoTracking()
                .Where(m => whStorageIds.Contains(m.ToStorageId) && !whStorageIds.Contains(m.FromStorageId) && m.CreatedAt >= since && m.ItemBlueprintId != null)
                .Select(m => new { m.ToStorageId, Blueprint = m.ItemBlueprintId!.Value, m.Amount }).ToListAsync(ct);

            var settings = await db.Set<ConsumerReplenishment>().AsNoTracking()
                .Where(s => consumerIds.Contains(s.StorageId) && s.Enabled).ToListAsync(ct);

            var consumerStructureIds = consumerStorages.Select(c => c.DomainId).Distinct().ToList();
            var inputs = await db.ProductionLineInputs.AsNoTracking()
                .Where(i => consumerStructureIds.Contains(i.ProductionLine.ProductionStructureId) && i.ProductionLine.Enabled)
                .Select(i => new { i.ProductionLine.ProductionStructureId, i.ItemBlueprintId, i.AmountPerHour }).ToListAsync(ct);
            var inputRates = inputs.GroupBy(i => i.ProductionStructureId)
                .ToDictionary(g => g.Key, g => g.GroupBy(x => x.ItemBlueprintId).ToDictionary(x => x.Key, x => x.Sum(y => y.AmountPerHour)));

            var blueprintIds = stock.Keys.Select(k => k.ItemBlueprintId)
                .Concat(open.Select(r => r.ItemBlueprintId)).Concat(settings.Select(s => s.ItemBlueprintId))
                .Concat(inputRates.Values.SelectMany(v => v.Keys)).Concat(inbound.Select(k => k.ItemBlueprintId))
                .Concat(ruleSet.Rules.Where(r => r.ItemBlueprintId.HasValue).Select(r => r.ItemBlueprintId!.Value))
                .Distinct().ToList();
            var items = await ItemsAsync(db, blueprintIds, ct);

            return new LogisticsTownState
            {
                TownId = town.Id,
                TownName = town.Name,
                Now = now,
                RuleSet = ruleSet,
                Warehouses = warehouses,
                WarehouseOfStorage = warehouseOf,
                StorageNames = storages.ToDictionary(s => s.Id, s => s.Name),
                Stock = stock,
                Places = places,
                ConsumerStorages = consumerStorages,
                OpenReservations = open,
                InboundLines = inbound,
                Outflow = outRows.GroupBy(r => (warehouseOf[r.FromStorageId], r.Blueprint)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)),
                Inflow = inRows.GroupBy(r => (warehouseOf[r.ToStorageId], r.Blueprint)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)),
                WindowDays = windowDays,
                Items = items,
                Settings = settings,
                InputRates = inputRates,
            };
        }

        public static async Task<Dictionary<int, LogisticsItem>> ItemsAsync(KnKDbContext db, IReadOnlyCollection<int> ids, CancellationToken ct)
        {
            var rows = await db.ItemBlueprints.AsNoTracking().Where(b => ids.Contains(b.Id))
                .Select(b => new { b.Id, b.Name, b.StorageWeight, b.CategoryId }).ToListAsync(ct);
            var tags = await db.Set<ItemBlueprintTag>().AsNoTracking().Where(t => ids.Contains(t.ItemBlueprintId))
                .Select(t => new { t.ItemBlueprintId, t.TagId }).ToListAsync(ct);
            var parents = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentCategoryId }).ToDictionaryAsync(c => c.Id, c => c.ParentCategoryId, ct);
            var result = new Dictionary<int, LogisticsItem>();
            foreach (var b in rows)
            {
                var chain = new HashSet<int>();
                for (int? c = b.CategoryId; c.HasValue && chain.Add(c.Value);) c = parents.GetValueOrDefault(c.Value);
                result[b.Id] = new LogisticsItem(b.Id, b.Name, b.StorageWeight > 0 ? b.StorageWeight : ItemBlueprint.DefaultStorageWeight, chain,
                    tags.Where(t => t.ItemBlueprintId == b.Id).Select(t => t.TagId).ToHashSet());
            }
            return result;
        }

        public string ItemName(int id) => Items.TryGetValue(id, out var i) ? i.Name : $"blueprint {id}";

        public string PlaceName(int id) => Places.TryGetValue(id, out var p) ? p.Name : $"structure {id}";

        public string WarehouseName(int id) => Warehouses.FirstOrDefault(w => w.Id == id)?.Name ?? $"warehouse {id}";

        // ================= Rule matching =================

        /// <summary>How well a rule's item target matches: Blueprint 3, Category 2, Tag 1, Any 0; null = no match.</summary>
        public int? ItemMatch(LogisticsRule r, int blueprint)
        {
            var item = Items.GetValueOrDefault(blueprint);
            return r.TargetKind switch
            {
                LogisticsTargetKind.Any => 0,
                LogisticsTargetKind.Blueprint => r.ItemBlueprintId == blueprint ? 3 : null,
                LogisticsTargetKind.Category => item != null && r.CategoryId.HasValue && item.CategoryIds.Contains(r.CategoryId.Value) ? 2 : null,
                LogisticsTargetKind.Tag => item != null && r.TagId.HasValue && item.TagIds.Contains(r.TagId.Value) ? 1 : null,
                _ => null,
            };
        }

        /// <summary>How well a rule's consumer target matches: Structure 2, ProductionKind 1, Any 0; null = no match.</summary>
        public int? ConsumerMatch(LogisticsRule r, int structure)
        {
            var p = Places.GetValueOrDefault(structure);
            return r.ConsumerKind switch
            {
                LogisticsConsumerKind.Any => 0,
                LogisticsConsumerKind.Structure => r.StructureId == structure ? 2 : null,
                LogisticsConsumerKind.ProductionKind => p?.ProductionKindId != null && r.ProductionKindId == p.ProductionKindId ? 1 : null,
                _ => null,
            };
        }

        /// <summary>The most specific matching rule of a type for a consumer and blueprint (consumer first, then item).</summary>
        public LogisticsRule? BestFor(LogisticsRuleType type, int structure, int blueprint) =>
            RuleSet.Of(type)
                .Select(r => (r, c: ConsumerMatch(r, structure), i: ItemMatch(r, blueprint)))
                .Where(x => x.c.HasValue && x.i.HasValue)
                .OrderByDescending(x => x.c!.Value * 10 + x.i!.Value).Select(x => x.r).FirstOrDefault();

        /// <summary>A reserve rule's units for a blueprint: units, or days of cover × the daily outflow.</summary>
        public long ReserveUnits(LogisticsRule r, decimal dailyOutflow) =>
            r.Units ?? (long)Math.Ceiling((r.DaysOfCover ?? 0m) * dailyOutflow);

        /// <summary>The town-wide reserve of a blueprint (the largest matching town-scope rule) and the rule.</summary>
        public (long Units, LogisticsRule? Rule) TownReserve(int blueprint)
        {
            decimal daily = DailyOutflowTown(blueprint);
            return RuleSet.Of(LogisticsRuleType.Reserve).Where(r => r.Scope == LogisticsRuleScope.Town && ItemMatch(r, blueprint).HasValue)
                .Select(r => (Units: ReserveUnits(r, daily), Rule: (LogisticsRule?)r)).OrderByDescending(x => x.Units).FirstOrDefault();
        }

        /// <summary>A warehouse's local reserve of a blueprint (the largest matching warehouse-scope rule) and the rule.</summary>
        public (long Units, LogisticsRule? Rule) WarehouseReserve(int warehouse, int blueprint)
        {
            decimal daily = DailyOutflow(warehouse, blueprint);
            return RuleSet.Of(LogisticsRuleType.Reserve)
                .Where(r => r.Scope == LogisticsRuleScope.Warehouse && r.WarehouseId == warehouse && ItemMatch(r, blueprint).HasValue)
                .Select(r => (Units: ReserveUnits(r, daily), Rule: (LogisticsRule?)r)).OrderByDescending(x => x.Units).FirstOrDefault();
        }

        /// <summary>A consumer storage's demand lines: its replenishment settings, else Replenishment rules (a blueprint
        /// target, or category, tag and Any for the blueprints its lines consume), else the line inputs × the default hours.</summary>
        public List<DemandSetting> DemandSettings(int storageId, int structureId, StoragePurpose purpose, ProductionLogisticsSettingsDto s)
        {
            var result = new Dictionary<int, DemandSetting>();
            var rates = purpose == StoragePurpose.Input ? InputRates.GetValueOrDefault(structureId) ?? new() : new Dictionary<int, decimal>();
            foreach (var (bid, rate) in rates.OrderBy(r => r.Key))
            {
                long reorder = (long)Math.Ceiling(rate * s.DefaultReorderHours);
                long target = Math.Max(reorder, (long)Math.Ceiling(rate * s.DefaultTargetHours));
                if (target > 0) result[bid] = new DemandSetting(bid, reorder, target, null, "Inputs", null, null);
            }
            var candidates = rates.Keys.Concat(RuleSet.Of(LogisticsRuleType.Replenishment).Where(r => r.TargetKind == LogisticsTargetKind.Blueprint && r.ItemBlueprintId.HasValue)
                .Select(r => r.ItemBlueprintId!.Value)).Distinct();
            foreach (var bid in candidates)
            {
                var rule = BestFor(LogisticsRuleType.Replenishment, structureId, bid);
                if (rule == null) continue;
                if (rule.TargetKind != LogisticsTargetKind.Blueprint && !rates.ContainsKey(bid)) continue;
                result[bid] = new DemandSetting(bid, rule.ReorderPoint ?? 0, rule.TargetLevel ?? 0, null, "Rule", null, rule);
            }
            foreach (var st in Settings.Where(x => x.StorageId == storageId))
                result[st.ItemBlueprintId] = new DemandSetting(st.ItemBlueprintId, st.ReorderPoint, st.TargetLevel, st.PriorityClass, "Setting", st.Id, null);
            return result.Values.Where(d => d.TargetLevel > 0).OrderBy(d => d.Blueprint).ToList();
        }

        /// <summary>The priority class of a consumer's demand line and the rule it came from.</summary>
        public (LogisticsPriorityClass Class, LogisticsRule? Rule, string Why) Priority(DemandSetting d, int structure)
        {
            if (d.PriorityClass.HasValue) return (d.PriorityClass.Value, null, "its replenishment setting");
            var rule = BestFor(LogisticsRuleType.PriorityClass, structure, d.Blueprint);
            return rule?.PriorityClass is { } pc ? (pc, rule, $"rule #{rule.Id}") : (LogisticsPriorityClass.Normal, null, "the default");
        }
    }

    /// <summary>Min/max for one blueprint of a consumer storage and where it came from (Setting, Rule or Inputs).</summary>
    public sealed record DemandSetting(int Blueprint, long ReorderPoint, long TargetLevel, LogisticsPriorityClass? PriorityClass,
        string Source, int? SettingId, LogisticsRule? Rule);

    public static class LogisticsPriority
    {
        public static int Rank(this LogisticsPriorityClass c) => c switch
        {
            LogisticsPriorityClass.Critical => 0,
            LogisticsPriorityClass.High => 1,
            LogisticsPriorityClass.Normal => 2,
            _ => 3,
        };
    }
}
