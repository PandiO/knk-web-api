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
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace knkwebapi_v2.Services.Production
{
    public interface ITransportService
    {
        Task<TransportOrderDto?> GetByIdAsync(int id, CancellationToken ct = default);

        Task<PagedResultDto<TransportOrderListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);

        /// <summary>A manual or quest order (works while Production:Enabled is off; it moves once the sweep runs).
        /// Throws <see cref="ArgumentException"/> (400) with the plan's errors.</summary>
        Task<TransportOrderDto> CreateAsync(TransportOrderCreateDto dto, int? actorUserId, CancellationToken ct = default);

        /// <summary>What <see cref="CreateAsync"/> would do, without writing anything.</summary>
        Task<TransportPlanDto> DryRunAsync(TransportOrderCreateDto dto, CancellationToken ct = default);

        /// <summary>Cancels an order: before loading it ends Cancelled; a loaded order turns back and returns its cargo
        /// (it ends Returned). 409 (<see cref="InvalidOperationException"/>) when final or already returning.</summary>
        Task<TransportOrderDto> CancelAsync(int id, int? actorUserId, string? reason, CancellationToken ct = default);

        /// <summary>Staff retry (P6a #683, M4): a waiting order (Planned, AtStop, AwaitingSpace, ReturnBlocked) tries its
        /// next step now; a Returned or Cancelled order is made again as a new manual order with the same stops (a repeated
        /// retry returns the same new order). 409 (<see cref="InvalidOperationException"/>) while Production:Enabled is off,
        /// for an order on the road (it arrives at its ETA) and for a delivered order.</summary>
        Task<TransportOrderDto> RetryAsync(int id, int? actorUserId, CancellationToken ct = default);

        /// <summary>A production structure's transport: the automatic trigger's dry run, open orders, workers away.</summary>
        Task<TransportStructureViewDto?> StructureViewAsync(int structureId, CancellationToken ct = default);

        /// <summary>The sweep: plans automatic orders and moves every due order along. Nothing while the flag is off (A21).
        /// <paramref name="all"/> looks at every open order, not only the due ones.</summary>
        Task<TransportSweepResultDto> SweepAsync(bool all = false, CancellationToken ct = default);

        /// <summary>Moves one order along up to now (sweep step for one order; no-op while the flag is off).</summary>
        Task<TransportOrderDto?> AdvanceAsync(int id, CancellationToken ct = default);
    }

    /// <summary>
    /// Transport core P5 (PandiO/knk-workspace#79, production DESIGN § 7). Orders carry cargo source → Transit storage →
    /// stops through <see cref="IStockService"/> (TransportLoad, TransportDeliver, TransportReturn), each step in one
    /// database transaction with the order row locked and an idempotency key per try, so a restart or a second API
    /// instance never applies a step twice and the ledger reconciles after every step. V1 carriers are simulated (G14):
    /// one of the carrier structure's workers is away from loading until the order ends (§ 7.7; the engine's worker
    /// factor lowers output meanwhile), and a leg takes route length ÷ walking speed. Overflow never drops cargo (V2
    /// defect 2): the carrier waits (AwaitingSpace), re-routes to another warehouse of the town, carries on, and brings
    /// back what is left (Returning, ReturnBlocked while the source is full).
    /// </summary>
    public sealed class TransportService : ITransportService
    {
        public const string Component = "TransportService";
        public const int SweepBatchSize = 200;
        public const int MaxStops = 10;
        public const int MaxLinesPerStop = 50;
        public const int MaxEventsShown = 500;
        public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);
        private const int MaxStepsPerAdvance = 25;

        private readonly KnKDbContext _db;
        private readonly IStockService _stock;
        private readonly IProductionSettingsReader _settings;
        private readonly IProductionEngine _engine;
        private readonly TransportRouting _routes;
        private readonly TimeProvider _clock;
        private readonly ILogger _logger;

        public TransportService(KnKDbContext db, IStockService stock, IProductionSettingsReader settings, IProductionEngine engine,
            TimeProvider? clock = null, ILogger<TransportService>? logger = null)
        {
            _db = db;
            _stock = stock;
            _settings = settings;
            _engine = engine;
            _routes = new TransportRouting(db);
            _clock = clock ?? TimeProvider.System;
            _logger = (ILogger?)logger ?? NullLogger.Instance;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        // ================= Reads =================

        public async Task<TransportOrderDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var o = await _db.TransportOrders.AsNoTracking().AsSplitQuery()
                .Include(x => x.SourceStorage).Include(x => x.CargoStorage).Include(x => x.SourceStructure).Include(x => x.CarrierStructure)
                .Include(x => x.Stops).ThenInclude(s => s.Lines).ThenInclude(l => l.ItemBlueprint)
                .Include(x => x.Stops).ThenInclude(s => s.Storage)
                .Include(x => x.Stops).ThenInclude(s => s.Structure)
                .Include(x => x.Lines).ThenInclude(l => l.ItemBlueprint)
                .Include(x => x.Assignments).ThenInclude(a => a.CarrierStructure)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o == null) return null;
            var events = await _db.TransportEvents.AsNoTracking().Where(e => e.TransportOrderId == id)
                .OrderBy(e => e.At).ThenBy(e => e.Id).Take(MaxEventsShown).ToListAsync(ct);
            var cargo = await _db.StockLines.AsNoTracking().Where(l => l.StorageId == o.CargoStorageId)
                .OrderBy(l => l.ItemBlueprintId)
                .Select(l => new TransportCargoDto { ItemBlueprintId = l.ItemBlueprintId, ItemBlueprintName = l.ItemBlueprint.Name, Amount = l.Amount })
                .ToListAsync(ct);
            return new TransportOrderDto
            {
                Id = o.Id,
                Origin = o.Origin.ToString(),
                Status = o.Status.ToString(),
                StatusReason = o.StatusReason,
                SourceStorageId = o.SourceStorageId,
                SourceStorage = Ref(o.SourceStorageId, o.SourceStorage?.Name),
                SourceStructureId = o.SourceStructureId,
                SourceStructure = o.SourceStructureId == null ? null : Ref(o.SourceStructureId.Value, o.SourceStructure?.Name),
                CargoStorageId = o.CargoStorageId,
                CargoStorage = Ref(o.CargoStorageId, o.CargoStorage?.Name),
                CarrierStructureId = o.CarrierStructureId,
                CarrierStructure = o.CarrierStructureId == null ? null : Ref(o.CarrierStructureId.Value, o.CarrierStructure?.Name),
                CarrierCapacity = o.CarrierCapacity,
                Risk = o.Risk,
                CurrentStructureId = o.CurrentStructureId,
                CurrentStopId = o.CurrentStopId,
                Reroutes = o.Reroutes,
                CancelRequested = o.CancelRequested,
                ExecuteAfter = o.ExecuteAfter,
                NextActionAt = o.NextActionAt,
                CreatedAt = o.CreatedAt,
                DepartedAt = o.DepartedAt,
                LegStartedAt = o.LegStartedAt,
                EtaAt = o.EtaAt,
                WaitingSince = o.WaitingSince,
                CompletedAt = o.CompletedAt,
                CreatedByUserId = o.CreatedByUserId,
                Note = o.Note,
                ClientKey = o.ClientKey,
                IsFinal = TransportOrder.IsFinal(o.Status),
                InTransit = cargo,
                Stops = o.Stops.OrderBy(s => s.Sequence).ThenBy(s => s.Id).Select(s => new TransportStopDto
                {
                    Id = s.Id,
                    Sequence = s.Sequence,
                    StorageId = s.StorageId,
                    Storage = Ref(s.StorageId, s.Storage?.Name),
                    StructureId = s.StructureId,
                    Structure = s.StructureId == null ? null : Ref(s.StructureId.Value, s.Structure?.Name),
                    Status = s.Status.ToString(),
                    ReroutedFromStopId = s.ReroutedFromStopId,
                    DistanceBlocks = s.DistanceBlocks,
                    DistanceSource = s.DistanceSource?.ToString(),
                    EtaAt = s.EtaAt,
                    ArrivedAt = s.ArrivedAt,
                    CompletedAt = s.CompletedAt,
                    Attempts = s.Attempts,
                    Lines = s.Lines.OrderBy(l => l.ItemBlueprintId).Select(l => new TransportStopLineDto
                    {
                        Id = l.Id, ItemBlueprintId = l.ItemBlueprintId, ItemBlueprintName = l.ItemBlueprint?.Name,
                        ItemBlueprint = Ref(l.ItemBlueprintId, l.ItemBlueprint?.Name),
                        Planned = l.Planned, Delivered = l.Delivered,
                    }).ToList(),
                }).ToList(),
                Lines = o.Lines.OrderBy(l => l.ItemBlueprintId).Select(l => new TransportOrderLineDto
                {
                    Id = l.Id, ItemBlueprintId = l.ItemBlueprintId, ItemBlueprintName = l.ItemBlueprint?.Name,
                    ItemBlueprint = Ref(l.ItemBlueprintId, l.ItemBlueprint?.Name),
                    Requested = l.Requested, Loaded = l.Loaded, Delivered = l.Delivered, Returned = l.Returned, Lost = l.Lost,
                }).ToList(),
                Assignments = o.Assignments.OrderBy(a => a.Id).Select(a => new TransportAssignmentDto
                {
                    Id = a.Id, Mode = a.Mode.ToString(), CarrierStructureId = a.CarrierStructureId,
                    CarrierStructure = a.CarrierStructureId == null ? null : Ref(a.CarrierStructureId.Value, a.CarrierStructure?.Name),
                    Workers = a.Workers, SpeedBlocksPerSecond = a.SpeedBlocksPerSecond, Capacity = a.Capacity,
                    AssignedAt = a.AssignedAt, ReleasedAt = a.ReleasedAt,
                }).ToList(),
                Events = events.Select(e => new TransportEventDto
                {
                    Id = e.Id, At = e.At, Kind = e.Kind.ToString(), StatusAfter = e.StatusAfter.ToString(),
                    TransportStopId = e.TransportStopId, StockTransactionId = e.StockTransactionId, Message = e.Message,
                }).ToList(),
            };
        }

        private static NamedRefDto Ref(int id, string? name) => new() { Id = id, Name = name ?? $"#{id}" };

        public async Task<PagedResultDto<TransportOrderListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<TransportOrder> q = _db.TransportOrders.AsNoTracking();
            if (Filter(query, "status") is { } st)
            {
                var statuses = st.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => Enum.TryParse<TransportStatus>(x, true, out var v) ? (TransportStatus?)v : null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (statuses.Count > 0) q = q.Where(o => statuses.Contains(o.Status));
            }
            if (Filter(query, "origin") is { } og && Enum.TryParse<TransportOrigin>(og, true, out var origin))
                q = q.Where(o => o.Origin == origin);
            if (Filter(query, "open") is { } open && bool.TryParse(open, out var isOpen))
                q = isOpen ? q.Where(o => o.NextActionAt != null) : q.Where(o => o.NextActionAt == null);
            if (TerritoryPaging.IntFilter(query, "sourceStructureId") is { } ss) q = q.Where(o => o.SourceStructureId == ss);
            if (TerritoryPaging.IntFilter(query, "sourceStorageId") is { } src) q = q.Where(o => o.SourceStorageId == src);
            if (TerritoryPaging.IntFilter(query, "storageId") is { } sto)
                q = q.Where(o => o.SourceStorageId == sto || o.Stops.Any(s => s.StorageId == sto));
            if (TerritoryPaging.IntFilter(query, "structureId") is { } str)
                q = q.Where(o => o.SourceStructureId == str || o.Stops.Any(s => s.StructureId == str)
                    || o.CarrierStructureId == str);
            if (TerritoryPaging.Term(query) is { } term && int.TryParse(term.TrimStart('#'), out var idTerm))
                q = q.Where(o => o.Id == idTerm);
            q = query.SortBy != null && !query.SortDescending ? q.OrderBy(o => o.Id) : q.OrderByDescending(o => o.Id);
            var page = await TerritoryPaging.PageAsync(q.Select(o => o.Id), query);
            var items = await ListAsync(page.Items, ct);
            return new PagedResultDto<TransportOrderListDto>
            {
                Items = items, TotalCount = page.TotalCount, PageNumber = page.PageNumber, PageSize = page.PageSize,
            };
        }

        private async Task<List<TransportOrderListDto>> ListAsync(IReadOnlyList<int> ids, CancellationToken ct)
        {
            var rows = await _db.TransportOrders.AsNoTracking().AsSplitQuery()
                .Include(o => o.SourceStorage).Include(o => o.SourceStructure)
                .Include(o => o.Stops).ThenInclude(s => s.Storage).Include(o => o.Lines)
                .Where(o => ids.Contains(o.Id)).ToListAsync(ct);
            var byId = rows.ToDictionary(o => o.Id);
            return ids.Where(byId.ContainsKey).Select(id => byId[id]).Select(o => new TransportOrderListDto
            {
                Id = o.Id,
                Origin = o.Origin.ToString(),
                Status = o.Status.ToString(),
                StatusReason = o.StatusReason,
                SourceStorageId = o.SourceStorageId,
                SourceStorageName = o.SourceStorage?.Name,
                SourceStructureId = o.SourceStructureId,
                SourceStructureName = o.SourceStructure?.Name,
                Destinations = string.Join(" → ", o.Stops.OrderBy(s => s.Sequence).Select(s => s.Storage?.Name ?? $"#{s.StorageId}")),
                UnitsRequested = o.Lines.Sum(l => l.Requested),
                UnitsLoaded = o.Lines.Sum(l => l.Loaded),
                UnitsDelivered = o.Lines.Sum(l => l.Delivered),
                UnitsReturned = o.Lines.Sum(l => l.Returned),
                CreatedAt = o.CreatedAt,
                EtaAt = o.EtaAt,
                CompletedAt = o.CompletedAt,
            }).ToList();
        }

        private static string? Filter(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        // ================= Plans =================

        private sealed class PlanContext
        {
            public TransportOrigin Origin { get; set; }
            public int? SourceStructureId { get; set; }
            public ProductionStructure? Carrier { get; set; }
            public Dictionary<int, long> Totals { get; } = new();
            public List<(int StorageId, int? StructureId, List<TransportLineRequestDto> Lines)> Stops { get; } = new();
        }

        public async Task<TransportPlanDto> DryRunAsync(TransportOrderCreateDto dto, CancellationToken ct = default)
        {
            var settings = (await _settings.GetAsync()).Transport!;
            var (plan, _) = await PlanAsync(dto, settings, manual: true, ct);
            return plan;
        }

        private async Task<(TransportPlanDto Plan, PlanContext? Ctx)> PlanAsync(TransportOrderCreateDto dto,
            ProductionTransportSettingsDto settings, bool manual, CancellationToken ct)
        {
            var plan = new TransportPlanDto { SourceStorageId = dto?.SourceStorageId ?? 0, SpeedBlocksPerSecond = settings.WalkingSpeedBlocksPerSecond };
            if (dto == null)
            {
                plan.Errors.Add("The order is empty.");
                return (plan, null);
            }
            var ctx = new PlanContext { Origin = TransportOrigin.Manual };
            if (!string.IsNullOrWhiteSpace(dto.Origin))
            {
                if (!Enum.TryParse<TransportOrigin>(dto.Origin.Trim(), true, out var origin) || !Enum.IsDefined(origin))
                    plan.Errors.Add("origin must be Manual or Quest.");
                else if (manual && origin is not (TransportOrigin.Manual or TransportOrigin.Quest))
                    plan.Errors.Add("origin must be Manual or Quest (automatic, distribution and trade orders are planned by the server).");
                else ctx.Origin = origin;
            }

            var source = await _db.Storages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == dto.SourceStorageId, ct);
            if (source == null) plan.Errors.Add($"Source storage {dto.SourceStorageId} not found.");
            else if (source.Kind is StorageKind.Virtual or StorageKind.Transit)
                plan.Errors.Add("The source must be a real storage (not a system or transit storage).");
            ctx.SourceStructureId = source == null ? null : await _routes.StructureOfStorageAsync(source.Id, ct);
            plan.SourceStructureId = ctx.SourceStructureId;

            // Stops and lines.
            var stops = dto.Stops ?? new List<TransportStopRequestDto>();
            if (stops.Count == 0) plan.Errors.Add("An order needs at least one stop.");
            if (stops.Count > MaxStops) plan.Errors.Add($"An order has at most {MaxStops} stops.");
            var blueprintIds = stops.SelectMany(s => s.Lines ?? new()).Select(l => l.ItemBlueprintId).Distinct().ToList();
            var names = await _db.ItemBlueprints.AsNoTracking().Where(b => blueprintIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
            foreach (var id in blueprintIds.Where(id => !names.ContainsKey(id))) plan.Errors.Add($"Item blueprint {id} not found.");
            int seq = 0;
            foreach (var stop in stops.Take(MaxStops))
            {
                seq++;
                var storage = await _db.Storages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == stop.StorageId, ct);
                if (storage == null) { plan.Errors.Add($"Stop {seq}: storage {stop.StorageId} not found."); continue; }
                if (storage.Kind is StorageKind.Virtual or StorageKind.Transit)
                    plan.Errors.Add($"Stop {seq}: a stop must be a real storage (not a system or transit storage).");
                if (storage.Id == dto.SourceStorageId) plan.Errors.Add($"Stop {seq}: the stop is the source storage.");
                var lines = (stop.Lines ?? new()).Where(l => l != null).ToList();
                if (lines.Count == 0) plan.Errors.Add($"Stop {seq}: at least one line (blueprint and amount).");
                if (lines.Count > MaxLinesPerStop) plan.Errors.Add($"Stop {seq}: at most {MaxLinesPerStop} lines.");
                if (lines.Any(l => l.Amount <= 0 || l.Amount > StockService.MaxAmount)) plan.Errors.Add($"Stop {seq}: every amount must be positive.");
                if (lines.GroupBy(l => l.ItemBlueprintId).Any(g => g.Count() > 1)) plan.Errors.Add($"Stop {seq}: a blueprint is listed twice.");
                var merged = lines.Where(l => l.Amount > 0).GroupBy(l => l.ItemBlueprintId)
                    .Select(g => new TransportLineRequestDto { ItemBlueprintId = g.Key, Amount = g.Sum(x => x.Amount) }).ToList();
                foreach (var l in merged) ctx.Totals[l.ItemBlueprintId] = ctx.Totals.GetValueOrDefault(l.ItemBlueprintId) + l.Amount;
                ctx.Stops.Add((storage.Id, await _routes.StructureOfStorageAsync(storage.Id, ct), merged));
            }

            // Carrier (§ 7.7): a production structure's worker; the source's structure by default.
            int? carrierId = dto.CarrierStructureId ?? ctx.SourceStructureId;
            if (carrierId.HasValue)
            {
                ctx.Carrier = await _db.ProductionStructures.AsNoTracking().FirstOrDefaultAsync(p => p.Id == carrierId, ct);
                if (ctx.Carrier == null && dto.CarrierStructureId.HasValue)
                    plan.Errors.Add($"Carrier structure {dto.CarrierStructureId} is not a production structure.");
            }
            var weights = await _routes.WeightsAsync(ctx.Totals.Keys, ct);
            decimal weighted = ctx.Totals.Sum(t => t.Value * weights.GetValueOrDefault(t.Key, ItemBlueprint.DefaultStorageWeight));
            if (ctx.Carrier != null)
            {
                plan.CarrierStructureId = ctx.Carrier.Id;
                plan.WorkersPresent = ctx.Carrier.WorkersPresent;
                plan.WorkersTotal = ctx.Carrier.WorkersTotal;
                plan.CarrierCapacity = ctx.Carrier.TransportCapacity ?? settings.DefaultCarrierCapacity;
                if (ctx.Carrier.WorkersTotal <= 0) plan.Errors.Add($"{ctx.Carrier.Name} has no workers to carry the order.");
                else if (ctx.Carrier.WorkersPresent <= 0)
                    plan.Warnings.Add($"No worker of {ctx.Carrier.Name} is free now (0 of {ctx.Carrier.WorkersTotal} present): the order waits.");
                if (weighted > plan.CarrierCapacity)
                    plan.Errors.Add($"The cargo is {weighted:0.##} units; a worker of {ctx.Carrier.Name} carries at most {plan.CarrierCapacity}.");
            }
            else if (ctx.Stops.Count > 0)
                plan.Warnings.Add("No production structure carries this order: a simulated carrier without a worker takes it.");

            // Lines and availability.
            foreach (var (bid, amount) in ctx.Totals.OrderBy(t => t.Key))
            {
                long available = source == null ? 0
                    : await _db.StockLines.AsNoTracking().Where(l => l.StorageId == source.Id && l.ItemBlueprintId == bid)
                        .Select(l => l.Amount).FirstOrDefaultAsync(ct);
                plan.Lines.Add(new TransportPlanLineDto
                {
                    ItemBlueprintId = bid, ItemBlueprintName = names.GetValueOrDefault(bid), Requested = amount, Available = available,
                });
                if (available < amount)
                    plan.Warnings.Add($"The source holds {available} of {amount} {names.GetValueOrDefault(bid) ?? $"blueprint {bid}"}: loading takes what is there.");
                plan.TotalUnits += amount;
            }

            // Legs.
            int? at = ctx.SourceStructureId;
            int legNo = 0;
            foreach (var (storageId, structureId, lines) in ctx.Stops)
            {
                legNo++;
                var d = await _routes.DistanceAsync(at, structureId, settings, ct);
                var room = await _routes.RoomAsync(storageId, ct);
                var leg = new TransportPlanLegDto
                {
                    Sequence = legNo, FromStructureId = at, ToStructureId = structureId, ToStorageId = storageId,
                    ToStorageName = await _db.Storages.AsNoTracking().Where(s => s.Id == storageId).Select(s => s.Name).FirstOrDefaultAsync(ct),
                    DistanceBlocks = d.Blocks, DistanceSource = d.Source.ToString(), Seconds = d.Seconds(settings.WalkingSpeedBlocksPerSecond),
                    Lines = lines, RoomUnits = room,
                };
                plan.Legs.Add(leg);
                plan.TotalSeconds += leg.Seconds;
                if (d.Source != TransportDistanceSource.Lane)
                    plan.Warnings.Add($"Leg {legNo}: no measured lane, the route is {(d.Source == TransportDistanceSource.Estimated ? "estimated from the straight distance" : "the fallback distance")} ({d.Blocks:0.#} blocks).");
                decimal need = lines.Sum(l => l.Amount * weights.GetValueOrDefault(l.ItemBlueprintId, ItemBlueprint.DefaultStorageWeight));
                if (room.HasValue && room.Value < need)
                    plan.Warnings.Add($"Stop {legNo} ({leg.ToStorageName}) has room for {room.Value:0.##} of {need:0.##} units now: the carrier would wait there.");
                at = structureId;
            }
            plan.Valid = plan.Errors.Count == 0;
            return (plan, plan.Valid ? ctx : null);
        }

        // ================= Create =================

        public async Task<TransportOrderDto> CreateAsync(TransportOrderCreateDto dto, int? actorUserId, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var key = string.IsNullOrWhiteSpace(dto.ClientKey) ? null : dto.ClientKey.Trim();
            if (key?.Length > TransportOrder.ClientKeyMaxLength)
                throw new ArgumentException($"clientKey is at most {TransportOrder.ClientKeyMaxLength} characters.");
            if (dto.Note?.Trim().Length > TransportOrder.NoteMaxLength)
                throw new ArgumentException($"note is at most {TransportOrder.NoteMaxLength} characters.");
            if (key != null && await _db.TransportOrders.AsNoTracking().Where(o => o.ClientKey == key).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct) is { } existing)
                return (await GetByIdAsync(existing, ct))!;

            var settings = (await _settings.GetAsync()).Transport!;
            var (plan, ctx) = await PlanAsync(dto, settings, manual: true, ct);
            if (ctx == null) throw new ArgumentException(string.Join(" ", plan.Errors));
            var now = Now;
            var executeAfter = dto.ExecuteAfter.HasValue ? DateTime.SpecifyKind(dto.ExecuteAfter.Value.ToUniversalTime(), DateTimeKind.Utc) : now;
            int id;
            try
            {
                id = await InsertAsync(ctx, plan, now, executeAfter, actorUserId, dto.Note?.Trim(), key,
                    $"Manual order by {(actorUserId.HasValue ? $"user {actorUserId}" : "the game server")}.", ct);
            }
            catch (DbUpdateException) when (key != null)
            {
                _db.ChangeTracker.Clear();
                // A concurrent request with the same key won the unique index.
                var winner = await _db.TransportOrders.AsNoTracking().Where(o => o.ClientKey == key).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
                if (winner == null) throw;
                id = winner.Value;
            }
            return (await GetByIdAsync(id, ct))!;
        }

        private async Task<int> InsertAsync(PlanContext ctx, TransportPlanDto plan, DateTime now, DateTime executeAfter,
            int? actorUserId, string? note, string? clientKey, string createdMessage, CancellationToken ct)
        {
            await using var tx = await BeginAsync(ct);
            var cargo = new Storage { Name = "Transport cargo", Kind = StorageKind.Transit, Purpose = StoragePurpose.General, CreatedAt = now };
            _db.Storages.Add(cargo);
            await _db.SaveChangesAsync(ct);
            var order = new TransportOrder
            {
                Origin = ctx.Origin,
                Status = TransportStatus.Planned,
                SourceStorageId = plan.SourceStorageId,
                SourceStructureId = ctx.SourceStructureId,
                CargoStorageId = cargo.Id,
                CarrierStructureId = ctx.Carrier?.Id,
                CarrierCapacity = plan.CarrierCapacity,
                CurrentStructureId = ctx.SourceStructureId,
                ExecuteAfter = executeAfter,
                NextActionAt = executeAfter,
                CreatedAt = now,
                CreatedByUserId = actorUserId,
                Note = string.IsNullOrEmpty(note) ? null : note,
                ClientKey = clientKey,
            };
            foreach (var (bid, amount) in ctx.Totals.OrderBy(t => t.Key))
                order.Lines.Add(new TransportOrderLine { ItemBlueprintId = bid, Requested = amount });
            int seq = 0;
            foreach (var (storageId, structureId, lines) in ctx.Stops)
            {
                var stop = new TransportStop { Sequence = ++seq, StorageId = storageId, StructureId = structureId };
                foreach (var l in lines) stop.Lines.Add(new TransportStopLine { ItemBlueprintId = l.ItemBlueprintId, Planned = l.Amount });
                order.Stops.Add(stop);
            }
            _db.TransportOrders.Add(order);
            await _db.SaveChangesAsync(ct);
            cargo.Name = $"Transport #{order.Id} cargo";
            var assignmentNote = ctx.Carrier == null ? "" : $" Carried by a worker of {ctx.Carrier.Name}.";
            AddEvent(order, now, TransportEventKind.Created, $"{createdMessage} {plan.TotalUnits} unit(s) to {ctx.Stops.Count} stop(s).{assignmentNote}");
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            return order.Id;
        }

        // ================= Cancel =================

        public async Task<TransportOrderDto> CancelAsync(int id, int? actorUserId, string? reason, CancellationToken ct = default)
        {
            var settings = (await _settings.GetAsync()).Transport!;
            var now = Now;
            string who = actorUserId.HasValue ? $"user {actorUserId}" : "the game server";
            string why = string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}";
            if (why.Length > 200) why = why[..200];
            await using (var tx = await BeginAsync(ct))
            {
                await LockOrderAsync(id, ct);
                var order = await LoadTrackedAsync(id, ct) ?? throw new KeyNotFoundException($"TransportOrder with id {id} not found.");
                if (TransportOrder.IsFinal(order.Status))
                    throw new InvalidOperationException($"The order is already {order.Status}.");
                if (order.Status is TransportStatus.Returning or TransportStatus.ReturnBlocked)
                    throw new InvalidOperationException("The order is already returning its cargo.");
                if (TransportOrder.IsLoaded(order.Status))
                {
                    order.CancelRequested = true;
                    AddEvent(order, now, TransportEventKind.CancelRequested, $"Cancelled by {who}{why}: the cargo goes back to the source.");
                    await StartReturnAsync(order, now, settings, "Cancelled: returning the cargo.", turnAround: order.Status == TransportStatus.InTransit, ct);
                }
                else
                {
                    // Nothing loaded yet (Planned; Assigned and Loading never outlive one step).
                    foreach (var s in order.Stops.Where(s => s.Status == TransportStopStatus.Pending)) s.Status = TransportStopStatus.Skipped;
                    order.Status = TransportStatus.Cancelled;
                    order.StatusReason = $"Cancelled by {who}{why}.";
                    order.CompletedAt = now;
                    order.NextActionAt = null;
                    order.EtaAt = null;
                    await ReleaseWorkersAsync(order, now, ct);
                    AddEvent(order, now, TransportEventKind.Cancelled, order.StatusReason);
                }
                await _db.SaveChangesAsync(ct);
                if (tx != null) await tx.CommitAsync(ct);
            }
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(id, ct))!;
        }

        // ================= Retry (P6a #683, M4) =================

        public async Task<TransportOrderDto> RetryAsync(int id, int? actorUserId, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            if (!settings.Enabled)
                throw new InvalidOperationException("Production is switched off (Production:Enabled); orders don't move.");
            var order = await _db.TransportOrders.AsNoTracking().Include(o => o.Stops).ThenInclude(s => s.Lines)
                .FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new KeyNotFoundException($"TransportOrder with id {id} not found.");
            string who = actorUserId.HasValue ? $"user {actorUserId}" : "the game server";
            switch (order.Status)
            {
                case TransportStatus.Delivered:
                    throw new InvalidOperationException("The order was delivered; there is nothing to retry.");
                case TransportStatus.Returned:
                case TransportStatus.Cancelled:
                {
                    var dto = new TransportOrderCreateDto
                    {
                        SourceStorageId = order.SourceStorageId,
                        CarrierStructureId = order.CarrierStructureId,
                        Origin = TransportOrigin.Manual.ToString(),
                        Note = $"Retry of transport #{order.Id}.",
                        ClientKey = $"retry-of-{order.Id}",
                        Stops = order.Stops.Where(st => st.Lines.Any(l => l.Planned > 0)).OrderBy(st => st.Sequence)
                            .Select(st => new TransportStopRequestDto
                            {
                                StorageId = st.StorageId,
                                Lines = st.Lines.Where(l => l.Planned > 0)
                                    .Select(l => new TransportLineRequestDto { ItemBlueprintId = l.ItemBlueprintId, Amount = l.Planned }).ToList(),
                            }).ToList(),
                    };
                    if (dto.Stops.Count == 0) throw new InvalidOperationException("The order has no cargo to send again.");
                    try
                    {
                        return await CreateAsync(dto, actorUserId, ct);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidOperationException($"The order can't be sent again: {ex.Message}");
                    }
                }
                case TransportStatus.InTransit:
                case TransportStatus.Returning:
                    throw new InvalidOperationException($"The order is on the road ({order.Status}); it arrives at {order.EtaAt:u}.");
            }
            if (order.NextActionAt == null) throw new InvalidOperationException($"The order is {order.Status} and has nothing to retry.");
            var now = Now;
            await using (var tx = await BeginAsync(ct))
            {
                await LockOrderAsync(id, ct);
                var tracked = await LoadTrackedAsync(id, ct) ?? throw new KeyNotFoundException($"TransportOrder with id {id} not found.");
                if (tracked.NextActionAt != null && tracked.NextActionAt > now)
                {
                    tracked.NextActionAt = now;
                    AddEvent(tracked, now, TransportEventKind.Note, $"Retried now by {who}.");
                }
                await _db.SaveChangesAsync(ct);
                if (tx != null) await tx.CommitAsync(ct);
            }
            _db.ChangeTracker.Clear();
            await AdvanceCoreAsync(id, now, settings.Transport!, ct);
            return (await GetByIdAsync(id, ct))!;
        }

        // ================= Sweep =================

        public async Task<TransportSweepResultDto> SweepAsync(bool all = false, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            var now = Now;
            var result = new TransportSweepResultDto { FeatureEnabled = settings.Enabled, RanAt = now };
            if (!settings.Enabled) return result;

            // 1. Automatic orders from producers (§ 7.4, V2 defect 7).
            var producers = await _db.ProductionStructures.AsNoTracking().Where(p => p.AutomaticTransport)
                .OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(ct);
            foreach (var pid in producers)
            {
                try
                {
                    if (await TriggerAsync(pid, settings.Transport!, now, ct)) result.OrdersCreated++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _db.ChangeTracker.Clear();
                    result.OrdersFailed++;
                    _logger.LogWarning(ex, "Automatic transport failed for production structure {StructureId}", pid);
                }
            }

            // 2. Every due order, oldest due first.
            var due = _db.TransportOrders.AsNoTracking().Where(o => o.NextActionAt != null);
            if (!all) due = due.Where(o => o.NextActionAt <= now);
            var ids = await due.OrderBy(o => o.NextActionAt).ThenBy(o => o.Id).Select(o => o.Id).Take(SweepBatchSize).ToListAsync(ct);
            foreach (var id in ids)
            {
                try
                {
                    if (await AdvanceCoreAsync(id, now, settings.Transport!, ct)) result.OrdersAdvanced++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _db.ChangeTracker.Clear();
                    result.OrdersFailed++;
                    _logger.LogWarning(ex, "Transport step failed for order {OrderId}", id);
                }
            }
            return result;
        }

        public async Task<TransportOrderDto?> AdvanceAsync(int id, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            if (!await _db.TransportOrders.AnyAsync(o => o.Id == id, ct)) return null;
            if (settings.Enabled) await AdvanceCoreAsync(id, Now, settings.Transport!, ct);
            return await GetByIdAsync(id, ct);
        }

        /// <summary>Steps the order until nothing more happens at <paramref name="now"/>; true when anything changed.</summary>
        private async Task<bool> AdvanceCoreAsync(int id, DateTime now, ProductionTransportSettingsDto settings, CancellationToken ct)
        {
            bool any = false;
            for (int i = 0; i < MaxStepsPerAdvance; i++)
            {
                var peek = await _db.TransportOrders.AsNoTracking().Where(o => o.Id == id)
                    .Select(o => new { o.Status, o.NextActionAt }).FirstOrDefaultAsync(ct);
                if (peek == null || peek.NextActionAt == null || peek.NextActionAt > now) break;
                // A step that sends a worker away or brings one back settles the carrier's production first, so the time
                // before counts at the old worker factor (§ 7.7). Waiting retries don't (at most one retry interval off).
                if (peek.Status is not (TransportStatus.AwaitingSpace or TransportStatus.ReturnBlocked))
                {
                    var carrier = await _db.TransportOrders.AsNoTracking().Where(o => o.Id == id)
                        .Select(o => o.CarrierStructureId).FirstOrDefaultAsync(ct);
                    if (carrier.HasValue) await _engine.SettleStructureAsync(carrier.Value, ct);
                }
                if (!await StepAsync(id, now, settings, ct)) break;
                any = true;
            }
            return any;
        }

        private async Task<bool> StepAsync(int id, DateTime now, ProductionTransportSettingsDto settings, CancellationToken ct)
        {
            bool progressed;
            await using (var tx = await BeginAsync(ct))
            {
                await LockOrderAsync(id, ct);
                var order = await LoadTrackedAsync(id, ct);
                if (order == null || order.NextActionAt == null || order.NextActionAt > now)
                {
                    if (tx != null) await tx.CommitAsync(ct);
                    _db.ChangeTracker.Clear();
                    return false;
                }
                var before = (order.Status, order.NextActionAt);
                switch (order.Status)
                {
                    case TransportStatus.Planned:
                    case TransportStatus.Assigned:
                    case TransportStatus.Loading:
                        await StartAsync(order, now, settings, ct);
                        break;
                    case TransportStatus.InTransit:
                        await ArriveAsync(order, now, settings, ct);
                        break;
                    case TransportStatus.AtStop:
                    case TransportStatus.AwaitingSpace:
                        await DeliverAsync(order, CurrentStop(order), now, settings, ct);
                        break;
                    case TransportStatus.Returning:
                    case TransportStatus.ReturnBlocked:
                        await ReturnCargoAsync(order, now, ct);
                        break;
                    default:
                        // Reserved states (robbery, stalled carriers) are not reached in V1.
                        order.NextActionAt = null;
                        break;
                }
                progressed = before != (order.Status, order.NextActionAt) || _db.ChangeTracker.HasChanges();
                await _db.SaveChangesAsync(ct);
                if (tx != null) await tx.CommitAsync(ct);
            }
            _db.ChangeTracker.Clear();
            return progressed;
        }

        private static TransportStop CurrentStop(TransportOrder order) =>
            order.Stops.FirstOrDefault(s => s.Id == order.CurrentStopId)
            ?? throw new InvalidOperationException($"Transport order {order.Id} has no current stop.");

        // ----- Planned → Assigned → Loading → InTransit -----

        private async Task StartAsync(TransportOrder order, DateTime now, ProductionTransportSettingsDto settings, CancellationToken ct)
        {
            int? carrierId = order.CarrierStructureId;
            ProductionStructure? carrier = carrierId == null ? null : await _db.ProductionStructures.FirstOrDefaultAsync(p => p.Id == carrierId, ct);
            if (carrier != null && (carrier.WorkersTotal <= 0 || carrier.WorkersPresent <= 0))
            {
                // § 7.7: no free worker, the order waits.
                string reason = carrier.WorkersTotal <= 0
                    ? $"{carrier.Name} has no workers: the order waits."
                    : $"Waiting for a free worker of {carrier.Name} (0 of {carrier.WorkersTotal} present).";
                if (order.StatusReason != reason)
                {
                    order.StatusReason = reason;
                    AddEvent(order, now, TransportEventKind.WaitingForWorker, reason);
                }
                order.NextActionAt = now + RetryInterval;
                return;
            }

            // Assigned: the worker leaves (the engine's worker factor drops while it is away).
            order.Assignments.Add(new TransportAssignment
            {
                Mode = TransportCarrierMode.Simulated,
                CarrierStructureId = carrier?.Id,
                Workers = carrier == null ? 0 : 1,
                SpeedBlocksPerSecond = settings.WalkingSpeedBlocksPerSecond,
                Capacity = order.CarrierCapacity,
                AssignedAt = now,
            });
            if (carrier != null) carrier.WorkersPresent = Math.Max(0, carrier.WorkersPresent - 1);
            order.Status = TransportStatus.Assigned;
            order.StatusReason = null;
            AddEvent(order, now, TransportEventKind.Assigned, carrier == null
                ? "A simulated carrier took the order."
                : $"A worker of {carrier.Name} took the order ({carrier.WorkersPresent} of {carrier.WorkersTotal} workers stay).");

            // Loading: source → transit, what is there up to the request (Partial).
            order.Status = TransportStatus.Loading;
            var requests = order.Lines.Where(l => l.Requested > l.Loaded).OrderBy(l => l.ItemBlueprintId)
                .Select(l => StockLineRequest.Fungible(order.SourceStorageId, order.CargoStorageId, l.ItemBlueprintId, l.Requested - l.Loaded))
                .ToList();
            StockResult? loaded = null;
            if (requests.Count > 0)
            {
                loaded = await _stock.MoveManyAsync(requests, Ctx(order, StockReason.TransportLoad, $"transport:{order.Id}:load", "Load"),
                    StockMoveMode.Partial, ct);
                foreach (var r in loaded.Lines)
                {
                    var line = order.Lines.First(l => l.ItemBlueprintId == r.ItemBlueprintId);
                    line.Loaded += r.Accepted;
                }
            }
            long total = order.Lines.Sum(l => l.Loaded);
            if (total <= 0)
            {
                foreach (var s in order.Stops) s.Status = TransportStopStatus.Skipped;
                order.Status = TransportStatus.Cancelled;
                order.StatusReason = "Nothing to load: the source holds none of the cargo.";
                order.CompletedAt = now;
                order.NextActionAt = null;
                AddEvent(order, now, TransportEventKind.Cancelled, order.StatusReason);
                await ReleaseWorkersAsync(order, now, ct);
                return;
            }
            var shortLines = order.Lines.Where(l => l.Loaded < l.Requested).ToList();
            AddEvent(order, now, TransportEventKind.Loaded,
                $"Loaded {total} of {order.Lines.Sum(l => l.Requested)} unit(s) into the order's transit storage."
                + (shortLines.Count > 0 ? " The source held less than requested; the first stops get theirs first." : ""),
                stockTransactionId: loaded?.TransactionId);
            // Shortfall: the stops in visiting order keep what was loaded (V2 defect 3: per-stop amounts stay true).
            foreach (var line in order.Lines)
            {
                long left = line.Loaded;
                foreach (var sl in order.Stops.OrderBy(s => s.Sequence).SelectMany(s => s.Lines.Where(x => x.ItemBlueprintId == line.ItemBlueprintId)))
                {
                    sl.Planned = Math.Min(sl.Planned, left);
                    left -= sl.Planned;
                }
            }
            foreach (var s in order.Stops.Where(s => s.Lines.All(l => l.Planned <= 0))) s.Status = TransportStopStatus.Skipped;

            order.DepartedAt = now;
            AddEvent(order, now, TransportEventKind.Departed, "Departed.");
            await NextStopAsync(order, now, settings, ct);
        }

        private async Task StartLegAsync(TransportOrder order, TransportStop stop, DateTime now, ProductionTransportSettingsDto settings,
            CancellationToken ct)
        {
            var d = await _routes.DistanceAsync(order.CurrentStructureId, stop.StructureId, settings, ct);
            long seconds = d.Seconds(settings.WalkingSpeedBlocksPerSecond);
            stop.DistanceBlocks = d.Blocks;
            stop.DistanceSource = d.Source;
            stop.EtaAt = now.AddSeconds(seconds);
            order.Status = TransportStatus.InTransit;
            order.StatusReason = null;
            order.CurrentStopId = stop.Id;
            order.LegStartedAt = now;
            order.EtaAt = stop.EtaAt;
            order.NextActionAt = stop.EtaAt;
            order.WaitingSince = null;
        }

        /// <summary>To the next pending stop; after the last one, cargo still on board goes back, else the order is done.</summary>
        private async Task NextStopAsync(TransportOrder order, DateTime now, ProductionTransportSettingsDto settings, CancellationToken ct)
        {
            var next = order.Stops.Where(s => s.Status == TransportStopStatus.Pending).OrderBy(s => s.Sequence).FirstOrDefault();
            if (next != null)
            {
                if (next.Id == 0) await _db.SaveChangesAsync(ct); // a stop added by a re-route needs its id
                await StartLegAsync(order, next, now, settings, ct);
                return;
            }
            if (InTransitTotal(order) > 0)
            {
                await StartReturnAsync(order, now, settings, "Cargo left after the last stop: it goes back to the source.", turnAround: false, ct);
                return;
            }
            await CompleteAsync(order, TransportStatus.Delivered, now, ct);
        }

        private static long InTransitTotal(TransportOrder order) => order.Lines.Sum(l => l.Loaded - l.Delivered - l.Returned - l.Lost);

        // ----- Stops -----

        private async Task ArriveAsync(TransportOrder order, DateTime now, ProductionTransportSettingsDto settings, CancellationToken ct)
        {
            var stop = CurrentStop(order);
            stop.ArrivedAt ??= now;
            order.CurrentStructureId = stop.StructureId ?? order.CurrentStructureId;
            order.Status = TransportStatus.AtStop;
            order.EtaAt = null;
            AddEvent(order, now, TransportEventKind.ArrivedAtStop, $"Arrived at stop {stop.Sequence}.", stop.Id);
            await DeliverAsync(order, stop, now, settings, ct);
        }

        private async Task DeliverAsync(TransportOrder order, TransportStop stop, DateTime now, ProductionTransportSettingsDto settings,
            CancellationToken ct)
        {
            var requests = new List<StockLineRequest>();
            foreach (var sl in stop.Lines.Where(l => l.Planned > l.Delivered).OrderBy(l => l.ItemBlueprintId))
            {
                var line = order.Lines.First(l => l.ItemBlueprintId == sl.ItemBlueprintId);
                long onBoard = line.Loaded - line.Delivered - line.Returned - line.Lost;
                long amount = Math.Min(sl.Planned - sl.Delivered, onBoard);
                if (amount > 0) requests.Add(StockLineRequest.Fungible(order.CargoStorageId, stop.StorageId, sl.ItemBlueprintId, amount));
            }
            if (requests.Count > 0)
            {
                stop.Attempts++;
                var moved = await _stock.MoveManyAsync(requests,
                    Ctx(order, StockReason.TransportDeliver, $"transport:{order.Id}:stop:{stop.Id}:try:{stop.Attempts}", $"Stop {stop.Sequence}"),
                    StockMoveMode.Partial, ct);
                long accepted = 0;
                foreach (var r in moved.Lines.Where(r => r.Accepted > 0))
                {
                    stop.Lines.First(l => l.ItemBlueprintId == r.ItemBlueprintId).Delivered += r.Accepted;
                    order.Lines.First(l => l.ItemBlueprintId == r.ItemBlueprintId).Delivered += r.Accepted;
                    accepted += r.Accepted;
                }
                if (accepted > 0)
                    AddEvent(order, now, TransportEventKind.Delivered, $"Delivered {accepted} unit(s) at stop {stop.Sequence}.", stop.Id, moved.TransactionId);
                var refused = moved.Lines.Where(r => r.Rejected > 0).ToList();
                if (refused.Count > 0)
                {
                    // V2 defect 2: overflow never drops. Wait, then re-route or carry on and bring the rest back.
                    string why = string.Join(", ", refused.Select(r => r.RejectReason).Distinct());
                    if (order.Status != TransportStatus.AwaitingSpace)
                    {
                        order.WaitingSince = now;
                        order.Status = TransportStatus.AwaitingSpace;
                        order.StatusReason = $"Stop {stop.Sequence} has no room for {refused.Sum(r => r.Rejected)} unit(s) ({why}): waiting.";
                        AddEvent(order, now, TransportEventKind.AwaitingSpace, order.StatusReason, stop.Id);
                    }
                    if (now - order.WaitingSince!.Value >= TimeSpan.FromMinutes(settings.AwaitingSpaceMinutes))
                    {
                        await RerouteOrCarryOnAsync(order, stop, now, settings, ct);
                        return;
                    }
                    order.NextActionAt = now + RetryInterval;
                    return;
                }
            }
            bool anyDelivered = stop.Lines.Any(l => l.Delivered > 0);
            stop.Status = stop.Lines.All(l => l.Delivered >= l.Planned)
                ? (anyDelivered ? TransportStopStatus.Delivered : TransportStopStatus.Skipped)
                : (anyDelivered ? TransportStopStatus.Partial : TransportStopStatus.Skipped);
            stop.CompletedAt = now;
            order.WaitingSince = null;
            await NextStopAsync(order, now, settings, ct);
        }

        private async Task RerouteOrCarryOnAsync(TransportOrder order, TransportStop stop, DateTime now,
            ProductionTransportSettingsDto settings, CancellationToken ct)
        {
            var remaining = stop.Lines.Where(l => l.Planned > l.Delivered)
                .Select(l => (l.ItemBlueprintId, Amount: l.Planned - l.Delivered)).ToList();
            stop.Status = stop.Lines.Any(l => l.Delivered > 0) ? TransportStopStatus.Partial : TransportStopStatus.Skipped;
            stop.CompletedAt = now;
            order.WaitingSince = null;

            Warehouse? target = null;
            if (order.Reroutes < settings.MaxReroutes)
            {
                var here = await _routes.PlaceAsync(stop.StructureId ?? order.SourceStructureId, ct);
                if (here?.TownId is { } town)
                {
                    var used = order.Stops.Select(s => s.StorageId).Append(order.SourceStorageId).ToHashSet();
                    var weights = await _routes.WeightsAsync(remaining.Select(r => r.ItemBlueprintId), ct);
                    decimal need = remaining.Sum(r => r.Amount * weights.GetValueOrDefault(r.ItemBlueprintId, ItemBlueprint.DefaultStorageWeight));
                    var candidates = new List<(Warehouse W, bool Fits, decimal Distance)>();
                    foreach (var w in await _routes.WarehousesOfTownAsync(town, ct))
                    {
                        if (used.Contains(w.DefaultStorageId!.Value)) continue;
                        var room = await _routes.RoomAsync(w.DefaultStorageId.Value, ct);
                        if (room is <= 0m) continue;
                        var d = await _routes.DistanceAsync(order.CurrentStructureId, w.Id, settings, ct);
                        candidates.Add((w, room == null || room >= need, d.Blocks));
                    }
                    target = candidates.OrderByDescending(c => c.Fits).ThenBy(c => c.Distance).ThenByDescending(c => c.W.Priority)
                        .ThenBy(c => c.W.Id).Select(c => c.W).FirstOrDefault();
                }
            }
            if (target == null)
            {
                AddEvent(order, now, TransportEventKind.Note,
                    $"No room at stop {stop.Sequence} after {settings.AwaitingSpaceMinutes} min and no other warehouse can take it: the carrier carries on and brings back what is left.",
                    stop.Id);
                await NextStopAsync(order, now, settings, ct);
                return;
            }
            foreach (var later in order.Stops.Where(s => s.Sequence > stop.Sequence)) later.Sequence++;
            var added = new TransportStop
            {
                Sequence = stop.Sequence + 1, StorageId = target.DefaultStorageId!.Value, StructureId = target.Id,
                ReroutedFromStopId = stop.Id,
            };
            foreach (var (bid, amount) in remaining) added.Lines.Add(new TransportStopLine { ItemBlueprintId = bid, Planned = amount });
            order.Stops.Add(added);
            order.Reroutes++;
            await _db.SaveChangesAsync(ct);
            AddEvent(order, now, TransportEventKind.Rerouted,
                $"No room at stop {stop.Sequence} after {settings.AwaitingSpaceMinutes} min: re-routed {remaining.Sum(r => r.Amount)} unit(s) to {target.Name}.",
                added.Id);
            await StartLegAsync(order, added, now, settings, ct);
        }

        // ----- Returning -----

        private async Task StartReturnAsync(TransportOrder order, DateTime now, ProductionTransportSettingsDto settings, string reason,
            bool turnAround, CancellationToken ct)
        {
            foreach (var s in order.Stops.Where(s => s.Status == TransportStopStatus.Pending && s.Id != order.CurrentStopId))
                s.Status = TransportStopStatus.Skipped;
            if (order.CurrentStopId is { } cur && order.Stops.FirstOrDefault(s => s.Id == cur) is { } current
                && current.Status == TransportStopStatus.Pending)
            {
                current.Status = current.Lines.Any(l => l.Delivered > 0) ? TransportStopStatus.Partial : TransportStopStatus.Skipped;
                current.CompletedAt = now;
            }
            long seconds;
            if (turnAround && order.LegStartedAt.HasValue)
                seconds = (long)Math.Ceiling(Math.Max(0, (now - order.LegStartedAt.Value).TotalSeconds));
            else
                seconds = (await _routes.DistanceAsync(order.CurrentStructureId, order.SourceStructureId, settings, ct))
                    .Seconds(settings.WalkingSpeedBlocksPerSecond);
            order.Status = TransportStatus.Returning;
            order.StatusReason = reason;
            order.CurrentStopId = null;
            order.LegStartedAt = now;
            order.EtaAt = now.AddSeconds(seconds);
            order.NextActionAt = order.EtaAt;
            order.WaitingSince = null;
            AddEvent(order, now, TransportEventKind.ReturnStarted, $"{reason} Back in {seconds} s.");
        }

        private async Task ReturnCargoAsync(TransportOrder order, DateTime now, CancellationToken ct)
        {
            order.CurrentStructureId = order.SourceStructureId;
            order.EtaAt = null;
            var requests = order.Lines.Where(l => l.Loaded - l.Delivered - l.Returned - l.Lost > 0).OrderBy(l => l.ItemBlueprintId)
                .Select(l => StockLineRequest.Fungible(order.CargoStorageId, order.SourceStorageId, l.ItemBlueprintId,
                    l.Loaded - l.Delivered - l.Returned - l.Lost)).ToList();
            if (requests.Count > 0)
            {
                order.ReturnAttempts++;
                var moved = await _stock.MoveManyAsync(requests,
                    Ctx(order, StockReason.TransportReturn, $"transport:{order.Id}:return:try:{order.ReturnAttempts}", "Return"),
                    StockMoveMode.Partial, ct);
                long accepted = 0;
                foreach (var r in moved.Lines.Where(r => r.Accepted > 0))
                {
                    order.Lines.First(l => l.ItemBlueprintId == r.ItemBlueprintId).Returned += r.Accepted;
                    accepted += r.Accepted;
                }
                if (accepted > 0)
                    AddEvent(order, now, TransportEventKind.Returned, $"Put {accepted} unit(s) back into the source.", stockTransactionId: moved.TransactionId);
                if (InTransitTotal(order) > 0)
                {
                    // The source is full: wait there (never drop, V2 defect 2).
                    if (order.Status != TransportStatus.ReturnBlocked)
                    {
                        order.Status = TransportStatus.ReturnBlocked;
                        order.WaitingSince = now;
                        order.StatusReason = $"The source has no room for {InTransitTotal(order)} unit(s): the carrier waits until it has.";
                        AddEvent(order, now, TransportEventKind.ReturnBlocked, order.StatusReason);
                    }
                    order.NextActionAt = now + RetryInterval;
                    return;
                }
            }
            await CompleteAsync(order, TransportStatus.Returned, now, ct);
        }

        // ----- End -----

        private async Task CompleteAsync(TransportOrder order, TransportStatus status, DateTime now, CancellationToken ct)
        {
            order.Status = status;
            order.StatusReason = order.CancelRequested ? "Cancelled: the cargo is back in the source." : order.StatusReason;
            if (status == TransportStatus.Delivered) order.StatusReason = null;
            order.CompletedAt = now;
            order.NextActionAt = null;
            order.EtaAt = null;
            order.CurrentStopId = null;
            order.WaitingSince = null;
            await ReleaseWorkersAsync(order, now, ct);
            AddEvent(order, now, TransportEventKind.Completed,
                $"{status}: {order.Lines.Sum(l => l.Delivered)} delivered, {order.Lines.Sum(l => l.Returned)} returned of {order.Lines.Sum(l => l.Loaded)} loaded.");
        }

        private async Task ReleaseWorkersAsync(TransportOrder order, DateTime now, CancellationToken ct)
        {
            foreach (var a in order.Assignments.Where(a => a.ReleasedAt == null))
            {
                a.ReleasedAt = now;
                if (a.Workers <= 0 || a.CarrierStructureId == null) continue;
                var carrier = await _db.ProductionStructures.FirstOrDefaultAsync(p => p.Id == a.CarrierStructureId, ct);
                if (carrier == null) continue;
                carrier.WorkersPresent = Math.Min(carrier.WorkersTotal, carrier.WorkersPresent + a.Workers);
                AddEvent(order, now, TransportEventKind.WorkerReturned,
                    $"The worker is back at {carrier.Name} ({carrier.WorkersPresent} of {carrier.WorkersTotal} present).");
            }
        }

        // ================= Automatic trigger (§ 7.4) =================

        private sealed record Evaluation(TransportStructureViewDto View, PlanContext? Ctx);

        public async Task<TransportStructureViewDto?> StructureViewAsync(int structureId, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            if (!await _db.ProductionStructures.AnyAsync(p => p.Id == structureId, ct)) return null;
            var view = (await EvaluateAsync(structureId, settings.Enabled, settings.Transport!, ct)).View;
            var open = await _db.TransportOrders.AsNoTracking()
                .Where(o => o.NextActionAt != null && (o.SourceStructureId == structureId || o.CarrierStructureId == structureId))
                .OrderBy(o => o.Id).Select(o => o.Id).ToListAsync(ct);
            view.OpenOrders = await ListAsync(open, ct);
            return view;
        }

        private async Task<Evaluation> EvaluateAsync(int structureId, bool featureEnabled, ProductionTransportSettingsDto settings,
            CancellationToken ct)
        {
            var p = await _db.ProductionStructures.AsNoTracking().Include(x => x.District).FirstAsync(x => x.Id == structureId, ct);
            var view = new TransportStructureViewDto
            {
                ProductionStructureId = p.Id,
                FeatureEnabled = featureEnabled,
                AutomaticTransport = p.AutomaticTransport,
                RoadConnection = p.RoadConnection.ToString(),
                IsRural = p.DistrictId == null,
                EffectiveTownId = p.District?.TownId ?? p.TownId,
                WorkersPresent = p.WorkersPresent,
                WorkersTotal = p.WorkersTotal,
                CarrierCapacity = p.TransportCapacity ?? settings.DefaultCarrierCapacity,
            };
            view.WorkersAway = await _db.TransportAssignments.AsNoTracking()
                .Where(a => a.CarrierStructureId == p.Id && a.ReleasedAt == null).SumAsync(a => a.Workers, ct);
            var output = await _db.Storages.AsNoTracking()
                .Where(s => s.Kind == StorageKind.Domain && s.DomainId == p.Id && s.Purpose == StoragePurpose.Output)
                .OrderBy(s => s.Id).FirstOrDefaultAsync(ct);

            // Warnings (A13): the missing road and the full storage, and what else stops transport.
            if (view.IsRural && p.RoadConnection == RoadConnectionState.Unconnected)
                view.Warnings.Add("Not connected to its town's road network: automatic transport is held (D5).");
            if (view.IsRural && p.RoadConnection == RoadConnectionState.Unmeasured)
                view.Warnings.Add("Its road connection has not been measured yet: automatic transport is held until the game server finds a road (D5).");
            if (p.WorkersTotal <= 0) view.Warnings.Add("It has no workers, so nobody can carry its goods.");
            if (output != null)
            {
                view.OutputStorageId = output.Id;
                view.OutputUnits = await _routes.UsedUnitsAsync(output.Id, ct);
                view.OutputMaxUnits = output.MaxUnits;
                view.Threshold = p.TransportThreshold ?? (output.MaxUnits.HasValue
                    ? Math.Max(1m, Math.Ceiling(output.MaxUnits.Value * settings.DefaultThresholdPercent / 100m))
                    : settings.DefaultThresholdUnits);
                if (output.MaxUnits.HasValue && view.OutputUnits >= output.MaxUnits.Value)
                    view.Warnings.Add($"The Output storage is full ({view.OutputUnits:0.##} of {output.MaxUnits} units): production stops until goods are carried away (D4).");
                if (output.MaxDistinct.HasValue && output.DistinctItems >= output.MaxDistinct.Value)
                    view.TriggerReasons.Add($"The Output storage holds as many different items as it may ({output.DistinctItems} of {output.MaxDistinct}).");
            }
            else view.Warnings.Add("It has no Output storage.");

            if (!featureEnabled) view.HeldReasons.Add("Production is turned off for the whole server (Production:Enabled).");
            if (!p.AutomaticTransport) view.HeldReasons.Add("Automatic transport is switched off for this structure.");
            if (view.IsRural && p.RoadConnection != RoadConnectionState.Connected)
                view.HeldReasons.Add(p.RoadConnection == RoadConnectionState.Unconnected
                    ? "Not connected to its town's road network (D5): no off-road transport."
                    : "Road connection not measured yet (D5).");
            if (view.EffectiveTownId == null) view.HeldReasons.Add("It has no town.");
            if (output == null) view.HeldReasons.Add("It has no Output storage.");
            if (p.WorkersTotal <= 0) view.HeldReasons.Add("It has no workers.");
            var openAuto = await _db.TransportOrders.AsNoTracking()
                .Where(o => o.SourceStructureId == p.Id && o.Origin == TransportOrigin.Automatic && o.NextActionAt != null)
                .Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
            if (openAuto != null) view.HeldReasons.Add($"Automatic order #{openAuto} is still under way (one at a time per structure).");

            // Triggers: occupancy, a line blocked full, the distinct cap (V2 defect 7).
            if (output != null)
            {
                if (view.OutputUnits >= view.Threshold && view.OutputUnits > 0)
                    view.TriggerReasons.Insert(0, $"The Output storage holds {view.OutputUnits:0.##} units, at or over the threshold of {view.Threshold:0.##}.");
                var blocked = await _db.ProductionLines.AsNoTracking()
                    .Where(l => l.ProductionStructureId == p.Id && l.Status == ProductionLineStatus.BlockedFull)
                    .Select(l => l.ItemBlueprint.Name).ToListAsync(ct);
                if (blocked.Count > 0) view.TriggerReasons.Add($"Line(s) blocked full: {string.Join(", ", blocked)}.");
            }

            // Sizing (V2 defect 8): the stock, largest first, up to the worker's capacity.
            var cargo = new List<TransportLineRequestDto>();
            if (output != null)
            {
                var stock = await _db.StockLines.AsNoTracking().Where(l => l.StorageId == output.Id && l.Amount > 0)
                    .Select(l => new { l.ItemBlueprintId, l.Amount, Weight = l.ItemBlueprint.StorageWeight }).ToListAsync(ct);
                decimal room = view.CarrierCapacity;
                foreach (var l in stock.OrderByDescending(x => x.Amount * (x.Weight > 0 ? x.Weight : 1m)).ThenBy(x => x.ItemBlueprintId))
                {
                    decimal w = l.Weight > 0 ? l.Weight : ItemBlueprint.DefaultStorageWeight;
                    long take = Math.Min(l.Amount, (long)Math.Floor(room / w));
                    if (take <= 0) continue;
                    cargo.Add(new TransportLineRequestDto { ItemBlueprintId = l.ItemBlueprintId, Amount = take });
                    room -= take * w;
                }
            }
            if (output != null && cargo.Count == 0) view.HeldReasons.Add("The Output storage holds nothing to carry.");

            // Destination (V2 defect 9): the preferred warehouse, else the nearest of the town by road, with room first.
            Warehouse? target = null;
            if (view.EffectiveTownId is { } town)
            {
                var weights = await _routes.WeightsAsync(cargo.Select(c => c.ItemBlueprintId), ct);
                decimal need = cargo.Sum(c => c.Amount * weights.GetValueOrDefault(c.ItemBlueprintId, ItemBlueprint.DefaultStorageWeight));
                var candidates = new List<(Warehouse W, bool Fits, bool Lane, decimal Distance)>();
                foreach (var w in await _routes.WarehousesOfTownAsync(town, ct))
                {
                    var room = await _routes.RoomAsync(w.DefaultStorageId!.Value, ct);
                    var d = await _routes.DistanceAsync(p.Id, w.Id, settings, ct);
                    candidates.Add((w, room == null || room >= need, d.Source == TransportDistanceSource.Lane, d.Blocks));
                }
                if (p.WarehousePreference == WarehousePreference.Preferred && p.PreferredWarehouseId is { } pref)
                {
                    var c = candidates.FirstOrDefault(x => x.W.Id == pref);
                    if (c.W != null && c.Fits) target = c.W;
                    else if (candidates.Count > 0)
                        view.Warnings.Add("The preferred warehouse can't take this delivery (not in the town, not accepting, or full): the nearest one is used.");
                }
                target ??= candidates.OrderByDescending(c => c.Fits).ThenBy(c => c.Distance).ThenByDescending(c => c.W.Priority)
                    .ThenBy(c => c.W.Id).Select(c => c.W).FirstOrDefault();
                if (target == null)
                {
                    view.HeldReasons.Add("No warehouse of its town takes deliveries.");
                    view.Warnings.Add("No warehouse of its town takes deliveries.");
                }
            }
            view.DestinationWarehouseId = target?.Id;

            if (view.TriggerReasons.Count == 0 || view.HeldReasons.Count > 0 || target == null || output == null || cargo.Count == 0)
                return new Evaluation(view, null);
            var (plan, ctx) = await PlanAsync(new TransportOrderCreateDto
            {
                SourceStorageId = output.Id,
                CarrierStructureId = p.Id,
                Stops = new List<TransportStopRequestDto> { new() { StorageId = target.DefaultStorageId!.Value, Lines = cargo } },
            }, settings, manual: false, ct);
            view.WouldCreate = plan;
            if (ctx != null) ctx.Origin = TransportOrigin.Automatic;
            return new Evaluation(view, ctx);
        }

        private async Task<bool> TriggerAsync(int structureId, ProductionTransportSettingsDto settings, DateTime now, CancellationToken ct)
        {
            var eval = await EvaluateAsync(structureId, true, settings, ct);
            if (eval.Ctx == null || eval.View.WouldCreate == null) return false;
            await using var tx = await BeginAsync(ct);
            // One open automatic order per source, also with two API instances: lock the structure, then check again.
            if (_db.Database.IsRelational())
                await _db.Database.ExecuteSqlRawAsync($"SELECT Id FROM production_structures WHERE Id = {structureId} FOR UPDATE", ct);
            if (await _db.TransportOrders.AnyAsync(o => o.SourceStructureId == structureId && o.Origin == TransportOrigin.Automatic
                    && o.NextActionAt != null, ct))
            {
                if (tx != null) await tx.CommitAsync(ct);
                return false;
            }
            string why = string.Join(" ", eval.View.TriggerReasons);
            await InsertAsync(eval.Ctx, eval.View.WouldCreate, now, now, null, null, null, $"Automatic: {why}", ct);
            if (tx != null) await tx.CommitAsync(ct);
            return true;
        }

        // ================= Helpers =================

        private StockContext Ctx(TransportOrder order, StockReason reason, string key, string what) => new()
        {
            Reason = reason,
            ActorKind = StockActorKind.System,
            ActorComponent = Component,
            SourceType = nameof(TransportOrder),
            SourceRef = order.Id.ToString(),
            Note = $"Transport #{order.Id}: {what}",
            IdempotencyKey = key,
        };

        private void AddEvent(TransportOrder order, DateTime at, TransportEventKind kind, string message, int? stopId = null,
            long? stockTransactionId = null)
        {
            if (message.Length > TransportEvent.MessageMaxLength) message = message[..TransportEvent.MessageMaxLength];
            var e = new TransportEvent
            {
                At = at, Kind = kind, StatusAfter = order.Status, TransportStopId = stopId, StockTransactionId = stockTransactionId,
                Message = message,
            };
            if (order.Id == 0) order.Events.Add(e);
            else
            {
                e.TransportOrderId = order.Id;
                _db.TransportEvents.Add(e);
            }
        }

        private Task<TransportOrder?> LoadTrackedAsync(int id, CancellationToken ct) =>
            _db.TransportOrders.AsSplitQuery()
                .Include(o => o.Stops).ThenInclude(s => s.Lines)
                .Include(o => o.Lines)
                .Include(o => o.Assignments)
                .FirstOrDefaultAsync(o => o.Id == id, ct);

        private async Task LockOrderAsync(int id, CancellationToken ct)
        {
            if (!_db.Database.IsRelational()) return;
            await _db.Database.ExecuteSqlRawAsync($"SELECT Id FROM transport_orders WHERE Id = {id} FOR UPDATE", ct);
        }

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
            _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;
    }
}
