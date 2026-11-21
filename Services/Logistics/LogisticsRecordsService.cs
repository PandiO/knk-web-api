using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Logistics
{
    public interface ILogisticsReservationService
    {
        Task<LogisticsReservationDto?> GetByIdAsync(int id, CancellationToken ct = default);

        /// <summary>Newest first; filters townId, warehouseId, consumerStructureId, consumerStorageId, itemBlueprintId,
        /// status, open (true/false), transportOrderId, plannerRunId.</summary>
        Task<PagedResultDto<LogisticsReservationListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);

        /// <summary>Staff release of an open reservation (Cancelled): its collection order is cancelled when it has not
        /// loaded yet. 409 when it is not open, or its worker already loaded the goods.</summary>
        Task<LogisticsReservationDto> ReleaseAsync(int id, int? actorUserId, string? reason, CancellationToken ct = default);
    }

    public interface ILogisticsPlannerRunService
    {
        /// <summary>A run with its plan explanation (one entry per consumer storage and blueprint with demand).</summary>
        Task<LogisticsPlannerRunDto?> GetByIdAsync(int id, CancellationToken ct = default);

        /// <summary>Newest first; filters townId, trigger.</summary>
        Task<PagedResultDto<LogisticsPlannerRunListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);
    }

    /// <summary>Reservations and planner runs (TOWN_LOGISTICS § 5, § 5.1): written by the planner, read here; staff can
    /// release an open reservation.</summary>
    public sealed class LogisticsRecordsService : ILogisticsReservationService, ILogisticsPlannerRunService
    {
        private readonly KnKDbContext _db;
        private readonly ITransportService _transport;
        private readonly TimeProvider _clock;

        public LogisticsRecordsService(KnKDbContext db, ITransportService transport, TimeProvider? clock = null)
        {
            _db = db;
            _transport = transport;
            _clock = clock ?? TimeProvider.System;
        }

        // ================= Reservations =================

        async Task<LogisticsReservationDto?> ILogisticsReservationService.GetByIdAsync(int id, CancellationToken ct) => await ReservationAsync(id, ct);

        private async Task<LogisticsReservationDto?> ReservationAsync(int id, CancellationToken ct)
        {
            var r = await _db.Set<LogisticsReservation>().AsNoTracking()
                .Include(x => x.Town).Include(x => x.Warehouse).Include(x => x.WarehouseStorage).Include(x => x.ConsumerStructure)
                .Include(x => x.ConsumerStorage).Include(x => x.ItemBlueprint).Include(x => x.TransportOrder)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r == null) return null;
            return new LogisticsReservationDto
            {
                Id = r.Id, TownId = r.TownId, Town = new NamedRefDto { Id = r.TownId, Name = r.Town.Name },
                WarehouseId = r.WarehouseId, Warehouse = new NamedRefDto { Id = r.WarehouseId, Name = r.Warehouse.Name },
                WarehouseStorageId = r.WarehouseStorageId, WarehouseStorage = new NamedRefDto { Id = r.WarehouseStorageId, Name = r.WarehouseStorage.Name },
                ConsumerStructureId = r.ConsumerStructureId, ConsumerStructure = new NamedRefDto { Id = r.ConsumerStructureId, Name = r.ConsumerStructure.Name },
                ConsumerStructureType = (await LogisticsStructureTypes.ResolveAsync(_db, new[] { r.ConsumerStructureId }, ct)).GetValueOrDefault(r.ConsumerStructureId),
                ConsumerStorageId = r.ConsumerStorageId, ConsumerStorage = new NamedRefDto { Id = r.ConsumerStorageId, Name = r.ConsumerStorage.Name },
                ItemBlueprintId = r.ItemBlueprintId, ItemBlueprint = new NamedRefDto { Id = r.ItemBlueprintId, Name = r.ItemBlueprint.Name },
                Amount = r.Amount, Collected = r.Collected, Status = r.Status.ToString(), PriorityClass = r.PriorityClass.ToString(),
                TransportOrderId = r.TransportOrderId, TransportOrderStatus = r.TransportOrder?.Status.ToString(), PlannerRunId = r.PlannerRunId,
                CreatedAt = r.CreatedAt, ExpiresAt = r.ExpiresAt, ClosedAt = r.ClosedAt, Explanation = r.Explanation, StatusReason = r.StatusReason,
            };
        }

        public static IQueryable<LogisticsReservationListDto> ReservationListQuery(IQueryable<LogisticsReservation> q) =>
            q.OrderByDescending(r => r.Id).Select(r => new LogisticsReservationListDto
            {
                Id = r.Id, TownId = r.TownId, TownName = r.Town.Name, WarehouseId = r.WarehouseId, WarehouseName = r.Warehouse.Name,
                ConsumerStructureId = r.ConsumerStructureId, ConsumerStructureName = r.ConsumerStructure.Name,
                ConsumerStructureType = r.ConsumerStructure is Shop ? LogisticsStructureTypes.Shop
                    : r.ConsumerStructure is ProductionStructure ? LogisticsStructureTypes.ProductionStructure
                    : r.ConsumerStructure is Warehouse ? LogisticsStructureTypes.Warehouse
                    : r.ConsumerStructure is GateStructure ? LogisticsStructureTypes.GateStructure
                    : LogisticsStructureTypes.Structure,
                ConsumerStorageId = r.ConsumerStorageId, ConsumerStorageName = r.ConsumerStorage.Name,
                WarehouseStorageId = r.WarehouseStorageId, WarehouseStorageName = r.WarehouseStorage.Name,
                ItemBlueprintId = r.ItemBlueprintId, ItemBlueprintName = r.ItemBlueprint.Name, Amount = r.Amount, Collected = r.Collected,
                Status = r.Status.ToString(), PriorityClass = r.PriorityClass.ToString(), TransportOrderId = r.TransportOrderId,
                CreatedAt = r.CreatedAt, ExpiresAt = r.ExpiresAt,
            });

        async Task<PagedResultDto<LogisticsReservationListDto>> ILogisticsReservationService.SearchAsync(PagedQueryDto query, CancellationToken ct)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<LogisticsReservation> q = _db.Set<LogisticsReservation>().AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "townId") is { } town) q = q.Where(r => r.TownId == town);
            if (TerritoryPaging.IntFilter(query, "warehouseId") is { } wh) q = q.Where(r => r.WarehouseId == wh);
            if (TerritoryPaging.IntFilter(query, "consumerStructureId") is { } cs) q = q.Where(r => r.ConsumerStructureId == cs);
            if (TerritoryPaging.IntFilter(query, "consumerStorageId") is { } st) q = q.Where(r => r.ConsumerStorageId == st);
            if (TerritoryPaging.IntFilter(query, "itemBlueprintId") is { } bp) q = q.Where(r => r.ItemBlueprintId == bp);
            if (TerritoryPaging.IntFilter(query, "transportOrderId") is { } o) q = q.Where(r => r.TransportOrderId == o);
            if (TerritoryPaging.IntFilter(query, "plannerRunId") is { } run) q = q.Where(r => r.PlannerRunId == run);
            if (Filter(query, "status") is { } s && Enum.TryParse<LogisticsReservationStatus>(s, true, out var status)) q = q.Where(r => r.Status == status);
            if (Filter(query, "open") is { } open && bool.TryParse(open, out var isOpen))
                q = isOpen ? q.Where(r => r.Status == LogisticsReservationStatus.Open) : q.Where(r => r.Status != LogisticsReservationStatus.Open);
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(r => r.ConsumerStructure.Name.ToLower().Contains(term) || r.ItemBlueprint.Name.ToLower().Contains(term) || r.Warehouse.Name.ToLower().Contains(term));
            return await TerritoryPaging.PageAsync(ReservationListQuery(q), query);
        }

        public async Task<LogisticsReservationDto> ReleaseAsync(int id, int? actorUserId, string? reason, CancellationToken ct = default)
        {
            var r = await _db.Set<LogisticsReservation>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"Reservation {id} not found.");
            if (r.Status != LogisticsReservationStatus.Open) throw new InvalidOperationException($"Reservation {id} is {r.Status}, not open.");
            if (reason?.Trim().Length > 300) throw new ArgumentException("reason is at most 300 characters.");
            if (r.TransportOrderId is { } orderId)
            {
                var order = await _db.TransportOrders.AsNoTracking().Include(o => o.Lines).FirstAsync(o => o.Id == orderId, ct);
                if (order.Lines.Any(l => l.Loaded > 0) || TransportOrder.IsLoaded(order.Status) || order.Status == TransportStatus.Delivered)
                    throw new InvalidOperationException($"The worker already loaded the goods (order #{orderId} is {order.Status}): the next planner run marks it collected.");
                if (!TransportOrder.IsFinal(order.Status))
                    await _transport.CancelAsync(orderId, actorUserId, $"Reservation #{id} released by staff.", ct);
            }
            var tracked = await _db.Set<LogisticsReservation>().FirstAsync(x => x.Id == id, ct);
            tracked.Status = LogisticsReservationStatus.Cancelled;
            tracked.OpenKey = null;
            tracked.ClosedAt = _clock.GetUtcNow().UtcDateTime;
            tracked.StatusReason = $"Released by {(actorUserId.HasValue ? $"user {actorUserId}" : "the game server")}{(string.IsNullOrWhiteSpace(reason) ? "." : $": {reason.Trim()}")}";
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            return (await ReservationAsync(id, ct))!;
        }

        // ================= Runs =================

        async Task<LogisticsPlannerRunDto?> ILogisticsPlannerRunService.GetByIdAsync(int id, CancellationToken ct)
        {
            var run = await _db.Set<LogisticsPlannerRun>().AsNoTracking().Include(r => r.Town).Include(r => r.Entries)
                .FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run == null) return null;
            var structureIds = run.Entries.Select(e => e.ConsumerStructureId).Concat(run.Entries.Where(e => e.WarehouseId.HasValue).Select(e => e.WarehouseId!.Value)).Distinct().ToList();
            var names = await _db.Structures.AsNoTracking().Where(s => structureIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            var storageIds = run.Entries.Select(e => e.ConsumerStorageId).Distinct().ToList();
            var storages = await _db.Storages.AsNoTracking().Where(s => storageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            var types = await LogisticsStructureTypes.ResolveAsync(_db, run.Entries.Select(e => e.ConsumerStructureId), ct);
            var blueprintIds = run.Entries.Select(e => e.ItemBlueprintId).Distinct().ToList();
            var items = await _db.ItemBlueprints.AsNoTracking().Where(b => blueprintIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
            return new LogisticsPlannerRunDto
            {
                Id = run.Id, TownId = run.TownId, Town = new NamedRefDto { Id = run.TownId, Name = run.Town.Name },
                LogisticsPolicyId = run.LogisticsPolicyId, PolicyVersion = run.PolicyVersion, Trigger = run.Trigger.ToString(),
                StartedAt = run.StartedAt, FinishedAt = run.FinishedAt, DemandLines = run.DemandLines, ReservationsCreated = run.ReservationsCreated,
                OrdersCreated = run.OrdersCreated, ReservationsReleased = run.ReservationsReleased, ReservationsCollected = run.ReservationsCollected,
                Shortfalls = run.Shortfalls, Summary = run.Summary, CreatedByUserId = run.CreatedByUserId,
                Entries = run.Entries.OrderBy(e => e.Id).Select(e => new LogisticsPlanEntryDto
                {
                    ConsumerStructureId = e.ConsumerStructureId, ConsumerStructureName = names.GetValueOrDefault(e.ConsumerStructureId),
                    ConsumerStructureType = types.GetValueOrDefault(e.ConsumerStructureId),
                    ConsumerStorageId = e.ConsumerStorageId, ConsumerStorageName = storages.GetValueOrDefault(e.ConsumerStorageId),
                    ItemBlueprintId = e.ItemBlueprintId, ItemBlueprintName = items.GetValueOrDefault(e.ItemBlueprintId),
                    WarehouseId = e.WarehouseId, WarehouseName = e.WarehouseId is { } w ? names.GetValueOrDefault(w) : null,
                    PriorityClass = e.PriorityClass.ToString(), Stock = e.Stock, ReorderPoint = e.ReorderPoint, TargetLevel = e.TargetLevel,
                    Shortfall = e.Shortfall, Reserved = e.Reserved, Outcome = e.Outcome.ToString(),
                    RuleIds = (e.RuleIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
                    ReservationId = e.LogisticsReservationId, TransportOrderId = e.TransportOrderId, Explanation = e.Explanation,
                }).ToList(),
            };
        }

        public static Expression<Func<LogisticsPlannerRun, LogisticsPlannerRunListDto>> RunListProjection() => r => new LogisticsPlannerRunListDto
        {
            Id = r.Id, TownId = r.TownId, TownName = r.Town.Name, Trigger = r.Trigger.ToString(), StartedAt = r.StartedAt,
            PolicyVersion = r.PolicyVersion, DemandLines = r.DemandLines, ReservationsCreated = r.ReservationsCreated, OrdersCreated = r.OrdersCreated,
            ReservationsReleased = r.ReservationsReleased, Shortfalls = r.Shortfalls, Summary = r.Summary,
        };

        async Task<PagedResultDto<LogisticsPlannerRunListDto>> ILogisticsPlannerRunService.SearchAsync(PagedQueryDto query, CancellationToken ct)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<LogisticsPlannerRun> q = _db.Set<LogisticsPlannerRun>().AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "townId") is { } town) q = q.Where(r => r.TownId == town);
            if (Filter(query, "trigger") is { } t && Enum.TryParse<LogisticsRunTrigger>(t, true, out var trigger)) q = q.Where(r => r.Trigger == trigger);
            return await TerritoryPaging.PageAsync(q.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Select(RunListProjection()), query);
        }

        private static string? Filter(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
    }
}
