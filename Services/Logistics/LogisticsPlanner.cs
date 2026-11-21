using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace knkwebapi_v2.Services.Logistics
{
    public interface ILogisticsPlanner
    {
        /// <summary>What a run would do now, without writing anything (works while Production:Enabled is off).</summary>
        Task<LogisticsPlanDto> DryRunAsync(int townId, CancellationToken ct = default);

        /// <summary>Runs the town's planner now. <see cref="ProductionOffException"/> (409) while Production:Enabled is off.</summary>
        Task<LogisticsPlanDto> RunAsync(int townId, int? actorUserId, CancellationToken ct = default);

        /// <summary>The schedule: plans every town with a warehouse whose last run is at least the interval ago, and
        /// prunes old runs. Nothing while the flag is off (A21).</summary>
        Task<LogisticsSweepResultDto> SweepAsync(CancellationToken ct = default);

        /// <summary>The town view (TOWN_LOGISTICS § 2): stock per blueprint across its warehouses, reserves, open
        /// reservations, available, inflow and outflow per day, days of cover.</summary>
        Task<TownSupplyDto> SupplyAsync(int townId, CancellationToken ct = default);

        /// <summary>Which warehouse serves each district, the rural territory and every structure (rule or nearest).</summary>
        Task<TownServiceAreasDto> ServiceAreasAsync(int townId, CancellationToken ct = default);

        /// <summary>A consumer as the planner sees it (min/max per storage and blueprint, worker, service warehouse).</summary>
        Task<ConsumerViewDto?> ConsumerAsync(int structureId, CancellationToken ct = default);
    }

    /// <summary>
    /// The town logistics planner (TOWN_LOGISTICS § 5, reserve and collect, D-L1): per town every 10 minutes (D-L2) and
    /// on demand with a dry run.
    /// <list type="number">
    /// <item>Settles reservations: collected ones (the order loaded), released ones (expired, F11, or the order ended).</item>
    /// <item>Per blueprint: available = stock − reserves − open reservations, per warehouse and town-wide.</item>
    /// <item>Demand: consumer storages at or below their reorder point (stock plus what is on the way).</item>
    /// <item>By priority class (Critical first), then by road distance; a reservation up to the target level at the
    ///   service area's warehouse, else the nearest one with stock.</item>
    /// <item>One collection order (origin Distribution) per consumer storage and warehouse storage, carried by the
    ///   consumer's own worker (§ 7.7) and sized to its capacity, made through <see cref="ITransportService"/> with an
    ///   idempotency key inside the run's transaction.</item>
    /// <item>A plan explanation per consumer storage and blueprint: which rule, which shortfall, which priority.</item>
    /// </list>
    /// One run per town at a time: the run locks the town row. Warehouses have no workers in V1.
    /// </summary>
    public sealed class LogisticsPlanner : ILogisticsPlanner
    {
        public const string Component = "LogisticsPlanner";
        public const int PruneBatch = 500;

        private readonly KnKDbContext _db;
        private readonly ITransportService _transport;
        private readonly IProductionSettingsReader _settings;
        private readonly TransportRouting _routes;
        private readonly TimeProvider _clock;
        private readonly ILogger _logger;

        public LogisticsPlanner(KnKDbContext db, ITransportService transport, IProductionSettingsReader settings, TimeProvider? clock = null,
            ILogger<LogisticsPlanner>? logger = null)
        {
            _db = db;
            _transport = transport;
            _settings = settings;
            _routes = new TransportRouting(db);
            _clock = clock ?? TimeProvider.System;
            _logger = (ILogger?)logger ?? NullLogger.Instance;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        // ================= Runs =================

        public async Task<LogisticsPlanDto> DryRunAsync(int townId, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            var now = Now;
            var state = await LogisticsTownState.LoadAsync(_db, townId, now, settings.Logistics!.ConsumptionWindowDays, ct);
            var plan = new LogisticsPlanDto { TownId = townId, TownName = state.TownName, FeatureEnabled = settings.Enabled, DryRun = true, At = now };
            var released = await SettleAsync(state, settings.Logistics!, plan, write: false, ct);
            await PlanAsync(state, released, settings, plan, ct);
            plan.Summary = Summary(plan);
            return plan;
        }

        public async Task<LogisticsPlanDto> RunAsync(int townId, int? actorUserId, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            if (!settings.Enabled) throw new ProductionOffException("Production is turned off (Production:Enabled): the planner does not run. Use the dry run.");
            if (!await _db.Towns.AnyAsync(t => t.Id == townId, ct)) throw new KeyNotFoundException($"Town {townId} not found.");
            return (await RunCoreAsync(townId, LogisticsRunTrigger.Manual, actorUserId, settings, ct))!;
        }

        public async Task<LogisticsSweepResultDto> SweepAsync(CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            var result = new LogisticsSweepResultDto { FeatureEnabled = settings.Enabled };
            if (!settings.Enabled) return result;
            var now = Now;
            var towns = await _db.Warehouses.AsNoTracking()
                .Select(w => w.District != null ? (int?)w.District.TownId : w.TownId).Where(t => t != null).Distinct().ToListAsync(ct);
            foreach (var town in towns.Select(t => t!.Value).OrderBy(t => t))
            {
                if (!await IsDueAsync(town, now, settings.Logistics!, ct)) continue;
                try
                {
                    var plan = await RunCoreAsync(town, LogisticsRunTrigger.Scheduled, null, settings, ct);
                    if (plan == null) continue;
                    result.TownsPlanned++;
                    result.ReservationsCreated += plan.Entries.Count(e => e.ReservationId != null);
                    result.OrdersCreated += plan.Orders.Count(o => o.TransportOrderId != null);
                    result.ReservationsReleased += plan.Released.Count;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _db.ChangeTracker.Clear();
                    result.Failed++;
                    _logger.LogWarning(ex, "Logistics planner failed for town {Town}", town);
                }
            }
            result.RunsPruned = await PruneAsync(now.AddDays(-settings.Logistics!.RunRetentionDays), ct);
            return result;
        }

        private async Task<bool> IsDueAsync(int townId, DateTime now, ProductionLogisticsSettingsDto s, CancellationToken ct)
        {
            var last = await _db.Set<LogisticsPlannerRun>().AsNoTracking().Where(r => r.TownId == townId)
                .OrderByDescending(r => r.StartedAt).Select(r => (DateTime?)r.StartedAt).FirstOrDefaultAsync(ct);
            return last == null || last.Value.AddMinutes(s.PlannerIntervalMinutes) <= now;
        }

        private async Task<int> PruneAsync(DateTime before, CancellationToken ct)
        {
            var old = await _db.Set<LogisticsPlannerRun>().Include(r => r.Entries).Where(r => r.StartedAt < before)
                .OrderBy(r => r.Id).Take(PruneBatch).ToListAsync(ct);
            if (old.Count == 0) return 0;
            _db.Set<LogisticsPlannerRun>().RemoveRange(old);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            return old.Count;
        }

        /// <summary>A real run. Null when a scheduled run found the town already planned by another instance.</summary>
        private async Task<LogisticsPlanDto?> RunCoreAsync(int townId, LogisticsRunTrigger trigger, int? actorUserId, ProductionSettingsDto settings,
            CancellationToken ct)
        {
            var s = settings.Logistics!;
            var now = Now;
            await using var tx = await BeginAsync(ct);
            if (_db.Database.IsRelational())
                await _db.Database.ExecuteSqlRawAsync($"SELECT Id FROM towns WHERE Id = {townId} FOR UPDATE", ct);
            if (trigger == LogisticsRunTrigger.Scheduled && !await IsDueAsync(townId, now, s, ct))
            {
                if (tx != null) await tx.CommitAsync(ct);
                return null;
            }
            var state = await LogisticsTownState.LoadAsync(_db, townId, now, s.ConsumptionWindowDays, ct);
            var run = new LogisticsPlannerRun
            {
                TownId = townId, Trigger = trigger, StartedAt = now, CreatedByUserId = actorUserId,
                LogisticsPolicyId = state.RuleSet.Policy?.Id, PolicyVersion = state.RuleSet.Policy?.Version,
            };
            _db.Set<LogisticsPlannerRun>().Add(run);
            await _db.SaveChangesAsync(ct);
            int runId = run.Id;
            _db.ChangeTracker.Clear();

            var plan = new LogisticsPlanDto { TownId = townId, TownName = state.TownName, FeatureEnabled = true, DryRun = false, At = now, RunId = runId };
            var released = await SettleAsync(state, s, plan, write: true, ct);
            var work = await PlanAsync(state, released, settings, plan, ct);
            await WriteAsync(state, work, plan, runId, s, ct);

            var tracked = await _db.Set<LogisticsPlannerRun>().FirstAsync(r => r.Id == runId, ct);
            tracked.FinishedAt = Now;
            tracked.DemandLines = plan.Entries.Count(e => e.Outcome != nameof(LogisticsPlanOutcome.NotNeeded));
            tracked.ReservationsCreated = plan.Entries.Count(e => e.ReservationId != null);
            tracked.OrdersCreated = plan.Orders.Count(o => o.TransportOrderId != null);
            tracked.ReservationsReleased = plan.Released.Count;
            tracked.ReservationsCollected = plan.Collected.Count;
            tracked.Shortfalls = plan.Entries.Count(e => e.Outcome is nameof(LogisticsPlanOutcome.PartlyReserved) or nameof(LogisticsPlanOutcome.NoStock)
                or nameof(LogisticsPlanOutcome.HeldByReserve) or nameof(LogisticsPlanOutcome.NoWarehouse) or nameof(LogisticsPlanOutcome.NoWorker)
                or nameof(LogisticsPlanOutcome.NotConnected) or nameof(LogisticsPlanOutcome.NoRoom) or nameof(LogisticsPlanOutcome.CarrierFull));
            plan.Summary = Summary(plan);
            tracked.Summary = Cut(plan.Summary, LogisticsPlannerRun.SummaryMaxLength);
            foreach (var e in plan.Entries.Where(e => e.Outcome != nameof(LogisticsPlanOutcome.NotNeeded)))
                tracked.Entries.Add(new LogisticsPlanEntry
                {
                    ConsumerStructureId = e.ConsumerStructureId, ConsumerStorageId = e.ConsumerStorageId, ItemBlueprintId = e.ItemBlueprintId,
                    WarehouseId = e.WarehouseId, PriorityClass = Enum.Parse<LogisticsPriorityClass>(e.PriorityClass), Stock = e.Stock,
                    ReorderPoint = e.ReorderPoint, TargetLevel = e.TargetLevel, Shortfall = e.Shortfall, Reserved = e.Reserved,
                    Outcome = Enum.Parse<LogisticsPlanOutcome>(e.Outcome),
                    RuleIds = e.RuleIds.Count == 0 ? null : Cut(string.Join(",", e.RuleIds), LogisticsPlanEntry.RuleIdsMaxLength),
                    LogisticsReservationId = e.ReservationId, TransportOrderId = e.TransportOrderId,
                    Explanation = Cut(e.Explanation, LogisticsPlanEntry.ExplanationMaxLength),
                });
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            return plan;
        }

        // ================= Step 1: settle reservations =================

        /// <summary>Collected (the order loaded), released (the order ended without loading, or expired, F11). Returns the
        /// ids no longer holding stock. With <paramref name="write"/> the changes are stored and expired orders cancelled.</summary>
        private async Task<HashSet<int>> SettleAsync(LogisticsTownState state, ProductionLogisticsSettingsDto s, LogisticsPlanDto plan, bool write,
            CancellationToken ct)
        {
            var closed = new HashSet<int>();
            var orderIds = state.OpenReservations.Where(r => r.TransportOrderId.HasValue).Select(r => r.TransportOrderId!.Value).Distinct().ToList();
            var orders = await _db.TransportOrders.AsNoTracking().Include(o => o.Lines).Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
            var toCancel = new HashSet<int>();
            foreach (var r in state.OpenReservations)
            {
                var order = r.TransportOrderId.HasValue ? orders.GetValueOrDefault(r.TransportOrderId.Value) : null;
                long loaded = order?.Lines.Where(l => l.ItemBlueprintId == r.ItemBlueprintId).Sum(l => l.Loaded) ?? 0;
                LogisticsReservationStatus? to = null;
                string reason = string.Empty;
                if (order != null && (loaded > 0 || TransportOrder.IsLoaded(order.Status) || order.Status == TransportStatus.Delivered))
                {
                    to = LogisticsReservationStatus.Collected;
                    reason = $"Collected by the worker of {state.PlaceName(r.ConsumerStructureId)} (order #{order.Id}).";
                    plan.Collected.Add(r.Id);
                }
                else if (order != null && TransportOrder.IsFinal(order.Status))
                {
                    to = LogisticsReservationStatus.Released;
                    reason = $"Its collection order #{order.Id} ended {order.Status} before loading.";
                }
                else if (r.ExpiresAt <= state.Now)
                {
                    to = LogisticsReservationStatus.Released;
                    reason = $"Not collected within {s.ReservationMinutes} minutes (expired {r.ExpiresAt:yyyy-MM-dd HH:mm} UTC): released at this run (F11).";
                    if (order != null && order.Status is TransportStatus.Planned or TransportStatus.Assigned) toCancel.Add(order.Id);
                }
                else if (order == null)
                {
                    to = LogisticsReservationStatus.Released;
                    reason = "It has no collection order.";
                }
                if (to == null) continue;
                closed.Add(r.Id);
                if (to != LogisticsReservationStatus.Collected && order != null) state.DroppedOrders.Add(order.Id);
                if (to != LogisticsReservationStatus.Collected)
                    plan.Released.Add(new LogisticsReleasedDto { ReservationId = r.Id, Status = to.Value.ToString(), Reason = reason });
                if (!write) continue;
                var tracked = await _db.Set<LogisticsReservation>().FirstAsync(x => x.Id == r.Id, ct);
                tracked.Status = to.Value;
                tracked.OpenKey = null;
                tracked.ClosedAt = state.Now;
                tracked.StatusReason = Cut(reason, LogisticsReservation.ReasonMaxLength);
                if (to == LogisticsReservationStatus.Collected) tracked.Collected = loaded;
            }
            if (write)
            {
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
                foreach (var id in toCancel)
                {
                    try
                    {
                        await _transport.CancelAsync(id, null, "Its reservation expired before the worker collected it (F11).", ct);
                    }
                    catch (InvalidOperationException)
                    {
                        // It moved on (loaded or final) meanwhile; the next run settles it.
                    }
                }
            }
            return closed;
        }

        // ================= Steps 2-6: the plan =================

        private sealed class Group
        {
            public int ConsumerStructureId;
            public int ConsumerStorageId;
            public int WarehouseId;
            public int WarehouseStorageId;
            public decimal CapacityLeft;
            public LogisticsPlannedOrderDto Order = null!;
            public List<(LogisticsPlanEntryDto Entry, LogisticsReservation Reservation)> Reservations { get; } = new();
        }

        private sealed class PlanWork
        {
            public List<Group> Groups { get; } = new();
        }

        private async Task<PlanWork> PlanAsync(LogisticsTownState state, HashSet<int> closed, ProductionSettingsDto settings, LogisticsPlanDto plan,
            CancellationToken ct)
        {
            var s = settings.Logistics!;
            var work = new PlanWork();
            plan.PolicyId = state.RuleSet.Policy?.Id;
            plan.PolicyName = state.RuleSet.Policy?.Name;
            plan.PolicyVersion = state.RuleSet.Policy?.Version;
            plan.PolicySource = state.RuleSet.Source;
            if (state.RuleSet.Source == "None") plan.Warnings.Add("The town has no logistics policy and there is no default template: no reserves or priority rules apply.");
            if (state.Warehouses.Count == 0) plan.Warnings.Add("The town has no warehouse.");

            // Held stock: open reservations that still hold.
            var held = new Dictionary<(int Storage, int Blueprint), long>();
            var openKeys = new HashSet<(int Storage, int Blueprint)>();
            foreach (var r in state.OpenReservations.Where(r => !closed.Contains(r.Id)))
            {
                held[(r.WarehouseStorageId, r.ItemBlueprintId)] = held.GetValueOrDefault((r.WarehouseStorageId, r.ItemBlueprintId)) + r.Amount;
                openKeys.Add((r.ConsumerStorageId, r.ItemBlueprintId));
            }
            long HeldWarehouse(int w, int b) => held.Where(h => h.Key.Blueprint == b && state.WarehouseOfStorage.GetValueOrDefault(h.Key.Storage) == w).Sum(h => h.Value);
            long HeldTown(int b) => held.Where(h => h.Key.Blueprint == b && state.WarehouseOfStorage.ContainsKey(h.Key.Storage)).Sum(h => h.Value);

            // Demand.
            var demands = new List<(LogisticsPlanEntryDto Entry, DemandSetting Setting, LogisticsPlace Place, LogisticsRule? PriorityRule, string PriorityWhy, decimal Distance, List<(Warehouse W, string How, LogisticsRule? Rule)> Candidates)>();
            var serviceCache = new Dictionary<int, List<(Warehouse W, string How, LogisticsRule? Rule, decimal Distance)>>();
            foreach (var (storageId, structureId, purpose) in state.ConsumerStorages)
            {
                var place = state.Places[structureId];
                foreach (var d in state.DemandSettings(storageId, structureId, purpose, s))
                {
                    long stock = state.Stock.GetValueOrDefault((storageId, d.Blueprint));
                    long inbound = state.InboundOf(storageId, d.Blueprint);
                    var (pc, prule, pwhy) = state.Priority(d, structureId);
                    var e = new LogisticsPlanEntryDto
                    {
                        ConsumerStructureId = structureId, ConsumerStructureName = place.Name,
                        ConsumerStructureType = state.StructureType(structureId), ConsumerStorageId = storageId,
                        ConsumerStorageName = state.StorageNames.GetValueOrDefault(storageId), ItemBlueprintId = d.Blueprint,
                        ItemBlueprintName = state.ItemName(d.Blueprint), PriorityClass = pc.ToString(), Stock = stock + inbound,
                        ReorderPoint = d.ReorderPoint, TargetLevel = d.TargetLevel, SettingSource = d.Source,
                    };
                    if (d.Rule != null) e.RuleIds.Add(d.Rule.Id);
                    if (prule != null) e.RuleIds.Add(prule.Id);
                    plan.Entries.Add(e);
                    string level = $"{e.ItemBlueprintName} at {place.Name} is {e.Stock} ({stock} in stock + {inbound} on the way); min {d.ReorderPoint}, max {d.TargetLevel} ({SourceText(d)})";
                    if (openKeys.Contains((storageId, d.Blueprint)))
                    {
                        e.Outcome = nameof(LogisticsPlanOutcome.AlreadyReserved);
                        e.Explanation = $"{level}. An open reservation is waiting for its worker.";
                        continue;
                    }
                    if (e.Stock > d.ReorderPoint)
                    {
                        e.Outcome = nameof(LogisticsPlanOutcome.NotNeeded);
                        e.Explanation = $"{level}: above the reorder point, nothing needed.";
                        continue;
                    }
                    e.Shortfall = d.TargetLevel - e.Stock;
                    string why = $"{level}: at or below the reorder point, short {e.Shortfall}; {pc} priority ({pwhy})";
                    if (!place.IsProduction || place.WorkersTotal <= 0)
                    {
                        e.Outcome = nameof(LogisticsPlanOutcome.NoWorker);
                        e.Explanation = $"{why}. It has no worker of its own to collect with (consumers collect with their own worker, D-L1).";
                        continue;
                    }
                    if (place.DistrictId == null && place.RoadConnection != RoadConnectionState.Connected)
                    {
                        e.Outcome = nameof(LogisticsPlanOutcome.NotConnected);
                        e.Explanation = $"{why}. A rural consumer without a road connection to its town ({place.RoadConnection}): no off-road collection (D5).";
                        continue;
                    }
                    var room = await _routes.RoomAsync(storageId, ct);
                    var weight = state.Items.GetValueOrDefault(d.Blueprint)?.Weight ?? ItemBlueprint.DefaultStorageWeight;
                    if (room.HasValue)
                    {
                        long fits = (long)Math.Floor(room.Value / weight);
                        if (fits <= 0)
                        {
                            e.Outcome = nameof(LogisticsPlanOutcome.NoRoom);
                            e.Explanation = $"{why}. Its storage has no room.";
                            continue;
                        }
                        if (fits < e.Shortfall) why += $"; its storage has room for {fits}";
                    }
                    if (state.Warehouses.Count == 0)
                    {
                        e.Outcome = nameof(LogisticsPlanOutcome.NoWarehouse);
                        e.Explanation = $"{why}. The town has no warehouse.";
                        continue;
                    }
                    if (!serviceCache.TryGetValue(structureId, out var cands))
                    {
                        cands = new();
                        var area = state.RuleSet.ServiceAreaFor(structureId, place.DistrictId);
                        foreach (var w in state.Warehouses)
                        {
                            var dist = await _routes.DistanceAsync(structureId, w.Id, settings.Transport!, ct);
                            cands.Add((w, area?.WarehouseId == w.Id ? $"its service area (rule #{area.Id})" : "nearest by road", area?.WarehouseId == w.Id ? area : null, dist.Blocks));
                        }
                        cands = cands.OrderBy(c => c.Rule == null ? 1 : 0).ThenBy(c => c.Distance).ThenByDescending(c => c.W.Priority).ThenBy(c => c.W.Id).ToList();
                        if (area == null && cands.Count > 0) cands[0] = (cands[0].W, "its service area (nearest by road)", null, cands[0].Distance);
                        serviceCache[structureId] = cands;
                    }
                    e.Explanation = why;
                    demands.Add((e, d, place, prule, pwhy, cands[0].Distance, cands.Select(c => (c.W, c.How, c.Rule)).ToList()));
                }
            }

            // Satisfy by priority class, then distance (lane cost).
            var capacityLeft = new Dictionary<int, int>(); // consumer structure → orders it may still start this run (one per worker)
            foreach (var (e, d, place, _, _, _, cands) in demands
                         .OrderBy(x => Enum.Parse<LogisticsPriorityClass>(x.Entry.PriorityClass).Rank()).ThenBy(x => x.Distance)
                         .ThenBy(x => x.Entry.ConsumerStructureId).ThenBy(x => x.Entry.ConsumerStorageId).ThenBy(x => x.Entry.ItemBlueprintId))
            {
                long need = e.Shortfall;
                var room = await _routes.RoomAsync(e.ConsumerStorageId, ct);
                decimal weight = state.Items.GetValueOrDefault(d.Blueprint)?.Weight ?? ItemBlueprint.DefaultStorageWeight;
                if (room.HasValue)
                {
                    decimal planned = work.Groups.Where(g => g.ConsumerStorageId == e.ConsumerStorageId)
                        .SelectMany(g => g.Order.Lines).Sum(l => l.Amount * (state.Items.GetValueOrDefault(l.ItemBlueprintId)?.Weight ?? 1m));
                    need = Math.Min(need, (long)Math.Floor(Math.Max(0m, room.Value - planned) / weight));
                }
                if (need <= 0)
                {
                    e.Outcome = nameof(LogisticsPlanOutcome.NoRoom);
                    e.Explanation = $"{e.Explanation}. Its storage has no room left after this run's other collections.";
                    continue;
                }
                var (townReserve, townRule) = state.TownReserve(d.Blueprint);
                long townAvail = Math.Max(0, state.TownStock(d.Blueprint) - townReserve - HeldTown(d.Blueprint));
                if (townRule != null) e.RuleIds.Add(townRule.Id);
                string reserveText = townRule == null ? "no town reserve" : $"town reserve {townReserve} (rule #{townRule.Id})";
                long best = 0;
                (Warehouse W, string How, LogisticsRule? Rule) chosen = default;
                int chosenStorage = 0;
                var notes = new List<string>();
                foreach (var c in cands)
                {
                    var (whReserve, whRule) = state.WarehouseReserve(c.W.Id, d.Blueprint);
                    long whStock = state.WarehouseStock(c.W.Id, d.Blueprint);
                    long whAvail = Math.Max(0, whStock - whReserve - HeldWarehouse(c.W.Id, d.Blueprint));
                    long take = Math.Min(need, Math.Min(whAvail, townAvail));
                    // The storage of the warehouse with the most free stock of it.
                    var storages = state.WarehouseOfStorage.Where(x => x.Value == c.W.Id).Select(x => x.Key)
                        .Select(st => (st, free: state.Stock.GetValueOrDefault((st, d.Blueprint)) - held.GetValueOrDefault((st, d.Blueprint))))
                        .OrderByDescending(x => x.free).ThenBy(x => x.st).ToList();
                    if (storages.Count == 0) { notes.Add($"{c.W.Name} has no storage"); continue; }
                    take = Math.Min(take, Math.Max(0, storages[0].free));
                    notes.Add($"{c.W.Name} ({c.How}): {whStock} in stock, {(whRule == null ? "no local reserve" : $"local reserve {whReserve} (rule #{whRule.Id})")}, {Math.Max(0, whAvail)} free");
                    if (whRule != null && !e.RuleIds.Contains(whRule.Id)) e.RuleIds.Add(whRule.Id);
                    if (c.Rule != null && !e.RuleIds.Contains(c.Rule.Id)) e.RuleIds.Add(c.Rule.Id);
                    // The first candidate (service area first, then by road) that covers the whole need; else the one
                    // that covers most.
                    if (take > best)
                    {
                        best = take;
                        chosen = c;
                        chosenStorage = storages[0].st;
                    }
                    if (take >= need) break;
                }
                if (best <= 0)
                {
                    e.Outcome = state.TownStock(d.Blueprint) == 0 ? nameof(LogisticsPlanOutcome.NoStock) : nameof(LogisticsPlanOutcome.HeldByReserve);
                    e.Explanation = e.Outcome == nameof(LogisticsPlanOutcome.NoStock)
                        ? $"{e.Explanation}. No warehouse of the town holds it."
                        : $"{e.Explanation}. Nothing free: {reserveText}, {HeldTown(d.Blueprint)} already reserved; {string.Join("; ", notes)}.";
                    continue;
                }

                // The consumer's worker carries it: one order per (consumer storage, warehouse storage), capacity-bound.
                var group = work.Groups.FirstOrDefault(g => g.ConsumerStorageId == e.ConsumerStorageId && g.WarehouseStorageId == chosenStorage);
                long capacity = place.TransportCapacity ?? settings.Transport!.DefaultCarrierCapacity;
                if (group == null)
                {
                    int startedOrders = work.Groups.Count(g => g.ConsumerStructureId == place.Id);
                    if (!capacityLeft.ContainsKey(place.Id)) capacityLeft[place.Id] = place.WorkersTotal;
                    if (startedOrders >= capacityLeft[place.Id])
                    {
                        e.Outcome = nameof(LogisticsPlanOutcome.CarrierFull);
                        e.Explanation = $"{e.Explanation}. Its {place.WorkersTotal} worker(s) already have a collection this run: next run.";
                        continue;
                    }
                    group = new Group
                    {
                        ConsumerStructureId = place.Id, ConsumerStorageId = e.ConsumerStorageId, WarehouseId = chosen.W.Id,
                        WarehouseStorageId = chosenStorage, CapacityLeft = capacity,
                        Order = new LogisticsPlannedOrderDto
                        {
                            ConsumerStructureId = place.Id, ConsumerStructureName = place.Name, ConsumerStructureType = state.StructureType(place.Id),
                            ConsumerStorageId = e.ConsumerStorageId, ConsumerStorageName = e.ConsumerStorageName,
                            WarehouseId = chosen.W.Id, WarehouseName = chosen.W.Name, WarehouseStorageId = chosenStorage,
                            WarehouseStorageName = state.StorageNames.GetValueOrDefault(chosenStorage), CarrierCapacity = capacity,
                            WorkersPresent = place.WorkersPresent, WorkersTotal = place.WorkersTotal,
                        },
                    };
                    if (place.WorkersPresent <= 0)
                        group.Order.Warnings.Add($"No worker of {place.Name} is free now: the collection waits until one is back.");
                    work.Groups.Add(group);
                    plan.Orders.Add(group.Order);
                }
                long carry = Math.Min(best, (long)Math.Floor(group.CapacityLeft / weight));
                if (carry <= 0)
                {
                    e.Outcome = nameof(LogisticsPlanOutcome.CarrierFull);
                    e.Explanation = $"{e.Explanation}. Its worker's capacity ({capacity}) is used by its other lines this run: next run.";
                    continue;
                }
                group.CapacityLeft -= carry * weight;
                group.Order.Lines.Add(new LogisticsPlannedOrderLineDto { ItemBlueprintId = d.Blueprint, ItemBlueprintName = e.ItemBlueprintName, Amount = carry });
                held[(chosenStorage, d.Blueprint)] = held.GetValueOrDefault((chosenStorage, d.Blueprint)) + carry;
                e.Reserved = carry;
                e.WarehouseId = chosen.W.Id;
                e.WarehouseName = chosen.W.Name;
                e.Outcome = carry >= e.Shortfall ? nameof(LogisticsPlanOutcome.Reserved) : nameof(LogisticsPlanOutcome.PartlyReserved);
                string limit = carry >= e.Shortfall ? "" : carry < best ? $" (its worker carries at most {capacity})"
                    : $" (only {best} free: {reserveText}{(need < e.Shortfall ? $"; room for {need}" : "")})";
                e.Explanation = $"{e.Explanation}. Reserved {carry} of {e.Shortfall} at {chosen.W.Name}, {chosen.How}{limit}; {reserveText}; "
                    + $"collected by its own worker."
                    + (chosen.W.Id == cands[0].W.Id ? "" : $" Warehouses: {string.Join("; ", notes)}.");
                group.Reservations.Add((e, new LogisticsReservation
                {
                    TownId = state.TownId, WarehouseId = chosen.W.Id, WarehouseStorageId = chosenStorage, ConsumerStructureId = place.Id,
                    ConsumerStorageId = e.ConsumerStorageId, ItemBlueprintId = d.Blueprint, Amount = carry, PriorityClass = Enum.Parse<LogisticsPriorityClass>(e.PriorityClass),
                    OpenKey = LogisticsReservation.KeyFor(e.ConsumerStorageId, d.Blueprint), CreatedAt = state.Now,
                    ExpiresAt = state.Now.AddMinutes(s.ReservationMinutes), Explanation = Cut(e.Explanation, LogisticsReservation.ReasonMaxLength),
                }));
            }
            foreach (var e in plan.Entries.Where(e => string.IsNullOrEmpty(e.Outcome)))
                e.Outcome = nameof(LogisticsPlanOutcome.NotNeeded);
            plan.Entries = plan.Entries
                .OrderBy(e => Enum.Parse<LogisticsPriorityClass>(e.PriorityClass).Rank()).ThenBy(e => e.ConsumerStructureId)
                .ThenBy(e => e.ConsumerStorageId).ThenBy(e => e.ItemBlueprintId).ToList();
            return work;
        }

        private static string SourceText(DemandSetting d) => d.Source switch
        {
            "Setting" => $"replenishment setting #{d.SettingId}",
            "Rule" => $"rule #{d.Rule?.Id}",
            _ => "from its production line inputs",
        };

        // ================= Write =================

        private async Task WriteAsync(LogisticsTownState state, PlanWork work, LogisticsPlanDto plan, int runId, ProductionLogisticsSettingsDto s,
            CancellationToken ct)
        {
            foreach (var g in work.Groups)
            {
                foreach (var (_, r) in g.Reservations)
                {
                    r.PlannerRunId = runId;
                    _db.Set<LogisticsReservation>().Add(r);
                }
                await _db.SaveChangesAsync(ct);
                var ids = g.Reservations.Select(x => x.Reservation.Id).ToList();
                for (int i = 0; i < ids.Count; i++) g.Reservations[i].Entry.ReservationId = ids[i];
                _db.ChangeTracker.Clear();

                var lines = g.Order.Lines.Select(l => $"{l.Amount} {state.ItemName(l.ItemBlueprintId)}");
                var (orderId, orderPlan) = await _transport.CreatePlannedAsync(new TransportOrderCreateDto
                {
                    SourceStorageId = g.WarehouseStorageId,
                    CarrierStructureId = g.ConsumerStructureId,
                    Note = Cut($"Collection for {state.PlaceName(g.ConsumerStructureId)} (logistics run #{runId}).", TransportOrder.NoteMaxLength),
                    Stops = new List<TransportStopRequestDto> { new() { StorageId = g.ConsumerStorageId, Lines = g.Order.Lines
                        .Select(l => new TransportLineRequestDto { ItemBlueprintId = l.ItemBlueprintId, Amount = l.Amount }).ToList() } },
                }, TransportOrigin.Distribution, $"logistics:{runId}:{g.ConsumerStorageId}:{g.WarehouseStorageId}",
                    $"Collection (logistics run #{runId}): {string.Join(", ", lines)} from {state.WarehouseName(g.WarehouseId)} for {state.PlaceName(g.ConsumerStructureId)}.", ct);
                g.Order.Warnings.AddRange(orderPlan.Warnings);
                var tracked = await _db.Set<LogisticsReservation>().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
                if (orderId == null)
                {
                    string why = $"The collection order could not be planned: {string.Join(" ", orderPlan.Errors)}";
                    g.Order.Warnings.Add(why);
                    foreach (var r in tracked)
                    {
                        r.Status = LogisticsReservationStatus.Released;
                        r.OpenKey = null;
                        r.ClosedAt = state.Now;
                        r.StatusReason = Cut(why, LogisticsReservation.ReasonMaxLength);
                        plan.Released.Add(new LogisticsReleasedDto { ReservationId = r.Id, Status = r.Status.ToString(), Reason = why });
                    }
                    foreach (var (e, _) in g.Reservations) e.Explanation = Cut($"{e.Explanation} {why}", LogisticsPlanEntry.ExplanationMaxLength);
                }
                else
                {
                    g.Order.TransportOrderId = orderId;
                    foreach (var r in tracked) r.TransportOrderId = orderId;
                    foreach (var (e, _) in g.Reservations) e.TransportOrderId = orderId;
                }
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
            }
        }

        private static string Summary(LogisticsPlanDto plan)
        {
            int demand = plan.Entries.Count(e => e.Outcome != nameof(LogisticsPlanOutcome.NotNeeded) && e.Outcome != nameof(LogisticsPlanOutcome.AlreadyReserved));
            int reserved = plan.Entries.Count(e => e.Reserved > 0);
            long units = plan.Entries.Sum(e => e.Reserved);
            int shorts = plan.Entries.Count(e => e.Shortfall > e.Reserved && e.Outcome != nameof(LogisticsPlanOutcome.NotNeeded) && e.Outcome != nameof(LogisticsPlanOutcome.AlreadyReserved));
            return $"{(plan.DryRun ? "Dry run" : "Run")} for {plan.TownName}: {demand} line(s) at or below their reorder point, {reserved} reserved ({units} units) "
                + $"in {plan.Orders.Count} collection order(s), {shorts} short; {plan.Released.Count} reservation(s) released, {plan.Collected.Count} collected. "
                + $"Policy: {(plan.PolicySource == "None" ? "none" : $"{plan.PolicyName} v{plan.PolicyVersion} ({plan.PolicySource})")}.";
        }

        // ================= Views =================

        public async Task<TownSupplyDto> SupplyAsync(int townId, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            var now = Now;
            var state = await LogisticsTownState.LoadAsync(_db, townId, now, settings.Logistics!.ConsumptionWindowDays, ct);
            var dto = new TownSupplyDto
            {
                TownId = townId, TownName = state.TownName, FeatureEnabled = settings.Enabled, At = now,
                ConsumptionWindowDays = state.WindowDays, PolicyId = state.RuleSet.Policy?.Id, PolicySource = state.RuleSet.Source,
                Warehouses = state.Warehouses.Select(w => new NamedRefDto { Id = w.Id, Name = w.Name }).ToList(),
            };
            var blueprints = state.WarehouseBlueprints().Concat(state.OpenReservations.Select(r => r.ItemBlueprintId))
                .Concat(state.Outflow.Keys.Select(k => k.Blueprint)).Concat(state.Inflow.Keys.Select(k => k.Blueprint)).Distinct().ToList();
            var missing = blueprints.Where(b => !state.Items.ContainsKey(b)).ToList();
            foreach (var kv in await LogisticsTownState.ItemsAsync(_db, missing, ct)) state.Items[kv.Key] = kv.Value;
            foreach (var b in blueprints.OrderBy(state.ItemName))
            {
                long stock = state.TownStock(b);
                long reserved = state.OpenReservations.Where(r => r.ItemBlueprintId == b).Sum(r => r.Amount);
                var (reserve, _) = state.TownReserve(b);
                decimal outflow = state.DailyOutflowTown(b);
                var line = new TownSupplyLineDto
                {
                    ItemBlueprintId = b, ItemBlueprintName = state.ItemName(b), Stock = stock, Reserved = reserved, ReserveUnits = reserve,
                    Available = Math.Max(0, stock - reserve - reserved),
                    OutflowPerDay = Math.Round(outflow, 2),
                    InflowPerDay = state.WindowDays <= 0 ? 0 : Math.Round((decimal)state.Inflow.Where(i => i.Key.Blueprint == b).Sum(i => i.Value) / state.WindowDays, 2),
                    DaysOfCover = outflow > 0 ? Math.Round(stock / outflow, 1) : null,
                };
                foreach (var w in state.Warehouses)
                {
                    long ws = state.WarehouseStock(w.Id, b);
                    long wr = state.OpenReservations.Where(r => r.ItemBlueprintId == b && r.WarehouseId == w.Id).Sum(r => r.Amount);
                    var (wres, _) = state.WarehouseReserve(w.Id, b);
                    line.Warehouses.Add(new TownSupplyWarehouseLineDto
                    {
                        WarehouseId = w.Id, WarehouseName = w.Name, Stock = ws, Reserved = wr, ReserveUnits = wres, Available = Math.Max(0, ws - wres - wr),
                    });
                }
                dto.Lines.Add(line);
            }
            dto.OpenReservations = await LogisticsRecordsService.ReservationListQuery(_db.Set<LogisticsReservation>().AsNoTracking()
                .Where(r => r.TownId == townId && r.Status == LogisticsReservationStatus.Open)).ToListAsync(ct);
            var last = await _db.Set<LogisticsPlannerRun>().AsNoTracking().Where(r => r.TownId == townId).OrderByDescending(r => r.StartedAt)
                .Select(LogisticsRecordsService.RunListProjection()).FirstOrDefaultAsync(ct);
            dto.LastRun = last;
            if (settings.Enabled && state.Warehouses.Count > 0)
                dto.NextRunAt = last == null ? now : last.StartedAt.AddMinutes(settings.Logistics!.PlannerIntervalMinutes);
            return dto;
        }

        public async Task<TownServiceAreasDto> ServiceAreasAsync(int townId, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            var now = Now;
            var state = await LogisticsTownState.LoadAsync(_db, townId, now, settings.Logistics!.ConsumptionWindowDays, ct);
            var dto = new TownServiceAreasDto { TownId = townId };
            var districts = await _db.Districts.AsNoTracking().Where(d => d.TownId == townId).OrderBy(d => d.Name).Select(d => new { d.Id, d.Name }).ToListAsync(ct);
            foreach (var d in districts)
            {
                var rule = state.RuleSet.Of(LogisticsRuleType.ServiceArea).FirstOrDefault(r => r.AreaKind == LogisticsAreaKind.District && r.DistrictId == d.Id);
                dto.Entries.Add(new ServiceAreaEntryDto
                {
                    AreaKind = nameof(LogisticsAreaKind.District), DistrictId = d.Id, DistrictName = d.Name,
                    WarehouseId = rule?.WarehouseId, WarehouseName = rule?.WarehouseId is { } w ? state.WarehouseName(w) : null,
                    Source = rule == null ? "Nearest" : "Rule", RuleId = rule?.Id,
                });
            }
            var rural = state.RuleSet.Of(LogisticsRuleType.ServiceArea).FirstOrDefault(r => r.AreaKind == LogisticsAreaKind.Rural);
            dto.Entries.Add(new ServiceAreaEntryDto
            {
                AreaKind = nameof(LogisticsAreaKind.Rural), WarehouseId = rural?.WarehouseId,
                WarehouseName = rural?.WarehouseId is { } rw ? state.WarehouseName(rw) : null, Source = rural == null ? "Nearest" : "Rule", RuleId = rural?.Id,
            });
            // Every structure that sends or receives goods: producers (Output) and consumers.
            var producers = await _db.ProductionStructures.AsNoTracking().Where(p => state.Places.Keys.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
            var ids = producers.Concat(state.ConsumerStorages.Select(c => c.StructureId)).Distinct().OrderBy(id => state.PlaceName(id)).ToList();
            foreach (var id in ids)
            {
                var place = state.Places[id];
                var rule = state.RuleSet.ServiceAreaFor(id, place.DistrictId);
                Warehouse? w = rule?.WarehouseId is { } rid ? state.Warehouses.FirstOrDefault(x => x.Id == rid) : null;
                if (w == null && state.Warehouses.Count > 0)
                {
                    decimal best = decimal.MaxValue;
                    foreach (var c in state.Warehouses)
                    {
                        var d = await _routes.DistanceAsync(id, c.Id, settings.Transport!, ct);
                        if (d.Blocks < best) (best, w) = (d.Blocks, c);
                    }
                }
                dto.Entries.Add(new ServiceAreaEntryDto
                {
                    AreaKind = nameof(LogisticsAreaKind.Structure), StructureId = id, StructureName = place.Name, StructureType = state.StructureType(id),
                    DistrictId = place.DistrictId,
                    WarehouseId = w?.Id, WarehouseName = w?.Name, Source = rule == null ? "Nearest" : "Rule", RuleId = rule?.Id,
                });
            }
            return dto;
        }

        public async Task<ConsumerViewDto?> ConsumerAsync(int structureId, CancellationToken ct = default)
        {
            var town = await _db.Structures.AsNoTracking().Where(s => s.Id == structureId)
                .Select(s => new { s.Id, TownId = s.District != null ? (int?)s.District.TownId : s.TownId }).FirstOrDefaultAsync(ct);
            if (town == null) return null;
            var settings = await _settings.GetAsync();
            var view = new ConsumerViewDto { StructureId = structureId, TownId = town.TownId };
            if (town.TownId == null)
            {
                view.StructureName = await _db.Structures.AsNoTracking().Where(s => s.Id == structureId).Select(s => s.Name).FirstAsync(ct);
                view.StructureType = (await LogisticsStructureTypes.ResolveAsync(_db, new[] { structureId }, ct)).GetValueOrDefault(structureId);
                view.Warnings.Add("It has no town: no warehouse supplies it.");
                return view;
            }
            var state = await LogisticsTownState.LoadAsync(_db, town.TownId.Value, Now, settings.Logistics!.ConsumptionWindowDays, ct);
            var place = state.Places[structureId];
            view.StructureName = place.Name;
            view.StructureType = state.StructureType(structureId);
            view.HasWorkers = place.IsProduction && place.WorkersTotal > 0;
            view.WorkersTotal = place.WorkersTotal;
            view.WorkersPresent = place.WorkersPresent;
            view.CarrierCapacity = place.TransportCapacity ?? settings.Transport!.DefaultCarrierCapacity;
            var rule = state.RuleSet.ServiceAreaFor(structureId, place.DistrictId);
            if (rule?.WarehouseId is { } wid)
            {
                view.ServiceWarehouseId = wid;
                view.ServiceWarehouseName = state.WarehouseName(wid);
                view.ServiceSource = $"Rule #{rule.Id}";
            }
            else view.ServiceSource = "Nearest";
            foreach (var (storageId, sid, purpose) in state.ConsumerStorages.Where(c => c.StructureId == structureId))
            {
                view.IsConsumer = true;
                var sv = new ConsumerStorageViewDto { StorageId = storageId, StorageName = state.StorageNames.GetValueOrDefault(storageId), Purpose = purpose.ToString() };
                foreach (var d in state.DemandSettings(storageId, sid, purpose, settings.Logistics!))
                {
                    var (pc, prule, _) = state.Priority(d, sid);
                    var line = new ConsumerDemandLineDto
                    {
                        ItemBlueprintId = d.Blueprint, ItemBlueprintName = state.ItemName(d.Blueprint), Stock = state.Stock.GetValueOrDefault((storageId, d.Blueprint)),
                        Inbound = state.InboundOf(storageId, d.Blueprint), ReorderPoint = d.ReorderPoint, TargetLevel = d.TargetLevel,
                        PriorityClass = pc.ToString(), SettingSource = d.Source, ConsumerReplenishmentId = d.SettingId,
                    };
                    if (d.Rule != null) line.RuleIds.Add(d.Rule.Id);
                    if (prule != null) line.RuleIds.Add(prule.Id);
                    sv.Lines.Add(line);
                }
                view.Storages.Add(sv);
            }
            if (!view.IsConsumer) view.Warnings.Add("It has no Input or Merchandise storage, so it is not a consumer.");
            else if (!view.HasWorkers) view.Warnings.Add("It has no worker of its own: the planner can't send a collection (consumers collect with their own worker).");
            if (view.IsConsumer && place.DistrictId == null && place.RoadConnection != RoadConnectionState.Connected)
                view.Warnings.Add("A rural consumer without a road connection: collections are held (D5).");
            view.OpenReservations = await LogisticsRecordsService.ReservationListQuery(_db.Set<LogisticsReservation>().AsNoTracking()
                .Where(r => r.ConsumerStructureId == structureId && r.Status == LogisticsReservationStatus.Open)).ToListAsync(ct);
            return view;
        }

        // ================= Helpers =================

        private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
            _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;
    }
}
